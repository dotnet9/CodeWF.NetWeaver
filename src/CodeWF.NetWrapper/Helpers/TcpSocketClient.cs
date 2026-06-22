using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Requests;
using CodeWF.NetWrapper.Response;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
///     TCP Socket 客户端类，用于与 TCP 服务器建立连接并进行通信，支持文件传输和命令收发
/// </summary>
public partial class TcpSocketClient
{
    private Socket? _client;
    private Channel<SocketCommand> _responses = Channel.CreateUnbounded<SocketCommand>();

    #region 公开属性

    /// <summary>
    ///     服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    ///     系统ID，用于标识客户端身份
    /// </summary>
    public long SystemId { get; private set; }

    /// <summary>
    ///     服务器IP地址
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    ///     服务器端口号
    /// </summary>
    public int ServerPort { get; private set; }

    /// <summary>
    ///     是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    ///     本地端点地址
    /// </summary>
    public string? LocalEndPoint { get; set; }

    /// <summary>
    ///     是否可以发送数据（需已连接且正在运行）
    /// </summary>
    public bool CanSend => _client is { Connected: true } && IsRunning;

    #endregion

    #region 公开接口

    /// <summary>
    ///     连接到TCP服务器
    /// </summary>
    /// <param name="serverMark">服务器标识</param>
    /// <param name="serverIP">服务器IP地址</param>
    /// <param name="serverPort">服务器端口号</param>
    /// <returns>连接结果和错误信息</returns>
    public async Task<(bool IsSuccess, string? ErrorMessage)> ConnectAsync(string serverMark, string serverIP,
        int serverPort)
    {
        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;

        try
        {
            var (responses, fileTransferResponses) = ResetResponseChannels();

            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _client.ConnectAsync(ipEndPoint);

            IsRunning = true;
            Logger.Info($"{ServerMark} 连接成功，服务地址是： {ServerIP}:{ServerPort}，当前客户端地址：{_client.LocalEndPoint}");

            _ = Task.Run(async () => await ListenForServerAsync(responses.Writer, fileTransferResponses.Writer));
            _ = Task.Run(async () => await CheckResponseAsync(responses.Reader, fileTransferResponses.Writer));
            _ = Task.Run(async () => await ProcessingFileTransferResponsesAsync(fileTransferResponses.Reader));

            LocalEndPoint = _client.LocalEndPoint?.ToString();
            return (IsSuccess: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            CloseConnection();
            Logger.Error($"{ServerMark} 连接异常 {ServerIP}:{ServerPort}", ex,
                $"{ServerMark} 连接异常 {ServerIP}:{ServerPort}，详细信息请查看日志文件");
            return (IsSuccess: false, ErrorMessage: $"{ServerMark} 连接异常 {ServerIP}:{ServerPort}，原因：{ex.Message}");
        }
    }

    /// <summary>
    ///     断开连接并停止客户端
    /// </summary>
    public void Stop()
    {
        CloseConnection();
        CompleteResponseChannels();
    }

    /// <summary>
    ///     发送命令到服务器
    /// </summary>
    /// <param name="command">要发送的网络对象命令</param>
    /// <exception cref="Exception">未连接时抛出异常</exception>
    public async Task SendCommandAsync(INetObject command)
    {
        var netObjInfo = command.GetType().GetNetObjectHead();
        if (!CanSend)
        {
            throw new Exception($"{ServerMark} 未连接，无法发送命令【ID：{netObjInfo.Id}，Version：{netObjInfo.Version}】");
        }

        var buffer = command.Serialize(SystemId);
        await _client!.SendAsync(buffer);
    }

    #endregion

    #region 连接TCP、接收数据

    /// <summary>
    ///     监听服务器消息（内部方法）
    /// </summary>
    private async Task ListenForServerAsync(ChannelWriter<SocketCommand> responses,
        ChannelWriter<SocketCommand> fileTransferResponses)
    {
        var socket = _client;
        var localEndPoint = socket?.LocalEndPoint?.ToString();
        while (IsRunning && ReferenceEquals(_client, socket) && socket?.Connected == true)
        {
            try
            {
                var (success, buffer, headInfo) = await socket.ReadPacketAsync();
                if (!success || buffer == null || headInfo == null)
                {
                    await HandleServerReceiveStoppedAsync(socket, responses, fileTransferResponses, localEndPoint);
                    break;
                }

                SystemId = headInfo.SystemId;
                await responses.WriteAsync(new SocketCommand(headInfo, buffer, socket));
            }
            catch (Exception ex)
            {
                await HandleServerReceiveStoppedAsync(socket, responses, fileTransferResponses, localEndPoint, ex);
                break;
            }
        }
    }

    private async Task HandleServerReceiveStoppedAsync(Socket? socket, ChannelWriter<SocketCommand> responses,
        ChannelWriter<SocketCommand> fileTransferResponses, string? localEndPoint, Exception? exception = null)
    {
        var shouldPublish = IsRunning && ReferenceEquals(_client, socket);
        var msg = exception == null
            ? $"{ServerMark} 服务端连接已断开，当前客户端地址：{localEndPoint}"
            : $"{ServerMark} 处理接收数据异常，当前客户端地址：{localEndPoint}";

        CloseConnection(socket);
        CompleteResponseChannels(responses, fileTransferResponses);

        if (!shouldPublish)
        {
            return;
        }

        if (exception == null)
        {
            Logger.Warn(msg);
        }
        else
        {
            Logger.Error(msg, exception, $"{msg}，详细信息请查看日志文件");
        }

        await EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(this, msg));
    }

    private void CloseConnection(Socket? expectedSocket = null)
    {
        if (expectedSocket != null && !ReferenceEquals(_client, expectedSocket))
        {
            return;
        }

        IsRunning = false;
        var socket = _client;
        _client = null;
        LocalEndPoint = null;
        socket?.CloseSocket();
    }

    private (Channel<SocketCommand> Responses, Channel<SocketCommand> FileTransferResponses) ResetResponseChannels()
    {
        _responses = Channel.CreateUnbounded<SocketCommand>();
        _fileTransferResponses = Channel.CreateUnbounded<SocketCommand>();
        return (_responses, _fileTransferResponses);
    }

    private void CompleteResponseChannels()
    {
        CompleteResponseChannels(_responses.Writer, _fileTransferResponses.Writer);
    }

    private static void CompleteResponseChannels(ChannelWriter<SocketCommand> responses,
        ChannelWriter<SocketCommand> fileTransferResponses)
    {
        responses.TryComplete();
        fileTransferResponses.TryComplete();
    }

    /// <summary>
    ///     检查响应队列并发布事件（内部方法）
    /// </summary>
    private async Task CheckResponseAsync(ChannelReader<SocketCommand> responses,
        ChannelWriter<SocketCommand> fileTransferResponses)
    {
        await foreach (var command in responses.ReadAllAsync())
        {
            if (command.IsCommand<FileUploadResponse>() ||
                command.IsCommand<FileDownloadResponse>() ||
                command.IsCommand<FileChunkData>() ||
                command.IsCommand<FileChunkAck>() ||
                command.IsCommand<FileTransferReject>() ||
                command.IsCommand<FileDownloadRequest>())
            {
                await fileTransferResponses.WriteAsync(command);
            }
            else
            {
                await EventBus.EventBus.Default.PublishAsync(command);
            }
        }
    }

    #endregion
}
