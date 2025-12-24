using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
///     TCP Socket 客户端类，用于与 TCP 服务器建立连接并进行通信
/// </summary>
public class TcpSocketClient
{
    /// <summary>
    ///     响应命令队列，用于存储接收到的响应命令
    /// </summary>
    public readonly BlockingCollection<SocketCommand> _responses = new(new ConcurrentQueue<SocketCommand>());

    private Socket? _client;


    #region 公开属性

    /// <summary>
    ///     服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    ///     服务端标识，TCP数据接收时保存，用于UDP数据包识别
    /// </summary>
    public long SystemId { get; private set; }

    /// <summary>
    ///     获取或设置服务器IP地址
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    ///     获取或设置服务器端口号
    /// </summary>
    public int ServerPort { get; private set; }


    /// <summary>
    ///     是否正在运行Tcp服务
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 本地端点连接信息
    /// </summary>
    public string? LocalEndPoint { get; set; }

    #endregion

    #region 公开接口

    /// <summary>
    ///     异步连接到TCP服务器
    /// </summary>
    /// <param name="serverMark">服务器标识</param>
    /// <param name="serverIP">服务器IP地址</param>
    /// <param name="serverPort">服务器端口号</param>
    /// <returns>连接结果，包含是否成功和错误信息</returns>
    public async Task<(bool IsSuccess, string? ErrorMessage)> ConnectAsync(string serverMark, string serverIP,
        int serverPort)
    {
        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;

        try
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _client.ConnectAsync(ipEndPoint);

            IsRunning = true;

            // 使用Task.Run并行运行监听器和响应检查器
            _ = Task.Run(ListenForServerAsync);
            _ = Task.Run(CheckResponseAsync);

            LocalEndPoint = _client.LocalEndPoint?.ToString();
            return (IsSuccess: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LocalEndPoint = null;
            Logger.Error($"{ServerMark} 连接异常 {ServerIP}:{ServerPort}", ex, $"{ServerMark} 连接异常 {ServerIP}:{ServerPort}，详细信息请查看日志文件");
            return (IsSuccess: false, ErrorMessage: $"{ServerMark} 连接异常 {ServerIP}:{ServerPort}，原因：{ex.Message}");
        }
    }

    /// <summary>
    ///     停止TCP客户端
    /// </summary>
    public void Stop()
    {
        IsRunning = false;
        _client?.CloseSocket();
        LocalEndPoint = null;
    }

    /// <summary>
    ///     异步发送命令到服务器
    /// </summary>
    /// <param name="command">要发送的命令对象</param>
    /// <exception cref="Exception">当客户端未连接时抛出</exception>
    public async Task SendCommandAsync(INetObject command)
    {
        if (!IsRunning || _client?.Connected != true) throw new Exception($"{ServerMark} 未连接，无法发送命令");

        var buffer = command.Serialize(SystemId);
        await _client!.SendAsync(buffer);
    }

    #endregion

    #region 连接TCP、接收数据

    /// <summary>
    ///     监听服务器发送的数据
    /// </summary>
    private async Task ListenForServerAsync()
    {
        while (IsRunning && _client?.Connected == true)
        {
            try
            {
                var (success, buffer, headInfo) = await _client!.ReadPacketAsync();
                if (!success) break;

                SystemId = headInfo.SystemId;
                _responses.Add(new SocketCommand(headInfo, buffer, _client));
            }
            catch (SocketException ex)
            {
                Logger.Error($"{ServerMark} 处理接收数据异常", ex, $"{ServerMark} 处理接收数据异常，详细信息请查看日志文件");
                break;
            }
            catch (Exception ex)
            {
                if (IsRunning) Logger.Error($"{ServerMark} 处理接收数据异常", ex, $"{ServerMark} 处理接收数据异常，详细信息请查看日志文件");

                break;
            }
        }
    }

    /// <summary>
    ///     检查响应命令队列
    /// </summary>
    private async Task CheckResponseAsync()
    {
        while (!IsRunning) await Task.Delay(TimeSpan.FromMilliseconds(10));

        while (IsRunning)
        {
            while (_responses.TryTake(out var command, TimeSpan.FromMilliseconds(10)))
                await EventBus.EventBus.Default.PublishAsync(command);
        }
    }

    #endregion
}