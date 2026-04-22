using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Models;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// TCP Socket 客户端类，用于与 TCP 服务器建立连接并进行通信，支持文件传输和命令收发
/// </summary>
public class TcpSocketClient
{
    private readonly Channel<SocketCommand> _responses = Channel.CreateUnbounded<SocketCommand>();
    private readonly Channel<SocketCommand> _fileTransferResponses = Channel.CreateUnbounded<SocketCommand>();
    private CancellationTokenSource? _fileTransferTokenSource;
    private Socket? _client;

    #region 公开属性

    /// <summary>
    /// 服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    /// 系统ID，用于标识客户端身份
    /// </summary>
    public long SystemId { get; private set; }

    /// <summary>
    /// 服务器IP地址
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    /// 服务器端口号
    /// </summary>
    public int ServerPort { get; private set; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 本地端点地址
    /// </summary>
    public string? LocalEndPoint { get; set; }

    /// <summary>
    /// 是否可以发送数据（需已连接且正在运行）
    /// </summary>
    public bool CanSend => _client is { Connected: true } && IsRunning;

    #endregion

    #region 公开接口

    /// <summary>
    /// 连接到TCP服务器
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
            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _client.ConnectAsync(ipEndPoint);

            IsRunning = true;
            Logger.Info($"{ServerMark} 连接成功，服务地址是： {ServerIP}:{ServerPort}，当前客户端地址：{_client.LocalEndPoint}");

            _fileTransferTokenSource = new CancellationTokenSource();
            _ = Task.Run(ListenForServerAsync);
            _ = Task.Run(CheckResponseAsync);
            _ = Task.Run(ProcessingFileTransferResponsesAsync);

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
    /// 断开连接并停止客户端
    /// </summary>
    public void Stop()
    {
        IsRunning = false;
        _responses.Writer.Complete();
        _client?.CloseSocket();
        LocalEndPoint = null;
    }

    /// <summary>
    /// 发送命令到服务器
    /// </summary>
    /// <param name="command">要发送的网络对象命令</param>
    /// <exception cref="Exception">未连接时抛出异常</exception>
    public async Task SendCommandAsync(INetObject command)
    {
        var netObjInfo = command.GetType().GetNetObjectHead();
        if (!CanSend) throw new Exception($"{ServerMark} 未连接，无法发送命令【ID：{netObjInfo.Id}，Version：{netObjInfo.Version}】");

        var buffer = command.Serialize(SystemId);
        await _client!.SendAsync(buffer);
    }

    #endregion

    #region 连接TCP、接收数据

    /// <summary>
    /// 监听服务器消息（内部方法）
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
                await _responses.Writer.WriteAsync(new SocketCommand(headInfo, buffer, _client));
            }
            catch (SocketException ex)
            {
                var msg = $"{ServerMark} 处理接收数据异常，当前客户端地址：{_client?.LocalEndPoint}";
                Logger.Error(msg, ex, $"{msg}，详细信息请查看日志文件");
                await EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(this, msg));
                break;
            }
            catch (Exception ex)
            {
                var msg = $"{ServerMark} 处理接收数据异常，当前客户端地址：{_client?.LocalEndPoint}";
                if (IsRunning)
                {
                    Logger.Error(msg, ex, $"{msg}，详细信息请查看日志文件");
                }
                await EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(this, msg));
                break;
            }
        }
    }

    /// <summary>
    /// 检查响应队列并发布事件（内部方法）
    /// </summary>
    private async Task CheckResponseAsync()
    {
        await foreach (var command in _responses.Reader.ReadAllAsync())
        {
            if (command.IsCommand<FileTransferStart>() ||
                command.IsCommand<FileTransferStartAck>() ||
                command.IsCommand<FileBlockData>() ||
                command.IsCommand<FileBlockAck>())
            {
                await _fileTransferResponses.Writer.WriteAsync(command);
            }
            else
            {
                await EventBus.EventBus.Default.PublishAsync(command);
            }
        }
    }

    /// <summary>
    /// 处理文件传输响应（内部方法，在独立线程中运行）
    /// </summary>
    private async Task ProcessingFileTransferResponsesAsync()
    {
        await foreach (var command in _fileTransferResponses.Reader.ReadAllAsync())
        {
            try
            {
                if (command.IsCommand<FileTransferStart>())
                {
                    var startInfo = command.GetCommand<FileTransferStart>();
                    await HandleFileTransferStartCommandAsync(startInfo);
                }
                else if (command.IsCommand<FileTransferStartAck>())
                {
                    var ack = command.GetCommand<FileTransferStartAck>();
                    await HandleFileTransferStartAckCommandAsync(ack);
                }
                else if (command.IsCommand<FileBlockAck>())
                {
                    var blockAck = command.GetCommand<FileBlockAck>();
                    await HandleFileBlockAckCommandAsync(blockAck);
                }
                else if (command.IsCommand<FileBlockData>())
                {
                    var blockData = command.GetCommand<FileBlockData>();
                    await HandleFileBlockDataAsync(blockData);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 处理文件传输响应异常", ex,
                    uiContent: $"{ServerMark} 处理文件传输响应异常，详细信息请查看日志文件");
            }
        }
    }

    /// <summary>
    /// 处理文件传输开始命令（内部方法）- 服务端请求下载
    /// </summary>
    private async Task HandleFileTransferStartCommandAsync(FileTransferStart startInfo)
    {
        var localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            startInfo.FileName);

        var context = new DownloadContext
        {
            FileName = startInfo.FileName,
            FileSize = startInfo.FileSize,
            FileHash = startInfo.FileHash,
            LocalFilePath = localPath,
            AlreadyTransferredBytes = startInfo.AlreadyTransferredBytes
        };
        _downloadContexts[startInfo.RemoteFilePath] = context;

        var ack = new FileTransferStartAck
        {
            Accept = true,
            AlreadyTransferredBytes = startInfo.AlreadyTransferredBytes
        };
        await SendCommandAsync(ack);
        Logger.Info($"{ServerMark} 收到服务端下载请求：{startInfo.FileName}，已传输：{startInfo.AlreadyTransferredBytes}字节，等待服务端发送文件块...");
    }

    /// <summary>
    /// 处理文件传输开始应答命令（内部方法）
    /// </summary>
    private async Task HandleFileTransferStartAckCommandAsync(FileTransferStartAck ack)
    {
        if (_uploadContexts.TryGetValue(ack.RemoteFilePath, out var context))
        {
            context.AlreadyTransferredBytes = ack.AlreadyTransferredBytes;
            Logger.Info($"{ServerMark} 收到服务端上传响应：已传输{ack.AlreadyTransferredBytes}字节，开始发送文件块...");
            await SendUploadBlockAsync(context.LocalFilePath, context.RemoteFilePath, context.FileSize, context.FileName, context.AlreadyTransferredBytes);
        }
        else
        {
            Logger.Warn($"{ServerMark} 收到服务端上传响应但无上传会话，忽略");
        }
    }

    /// <summary>
    /// 处理文件块传输应答命令（内部方法）
    /// </summary>
    private async Task HandleFileBlockAckCommandAsync(FileBlockAck blockAck)
    {
        if (blockAck.Success && _uploadContexts.TryGetValue(blockAck.RemoteFilePath, out var context))
        {
            var nextOffset = blockAck.AlreadyTransferredBytes;
            await SendUploadBlockAsync(context.LocalFilePath, context.RemoteFilePath, context.FileSize, context.FileName, nextOffset);
        }
        else if (!blockAck.Success)
        {
            Logger.Error($"{ServerMark} 服务器报告文件块传输失败：{blockAck.Message}");
        }
    }

    #endregion

    #region 文件传输接口（断点续传）

    /// <summary>
    /// 文件传输块大小（64KB），每次传输的数据块大小
    /// </summary>
    public const int FileTransferBlockSize = 64 * 1024;

    /// <summary>
    /// 文件传输进度事件，当文件传输进度更新时触发
    /// </summary>
    public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgress;

    private readonly ConcurrentDictionary<string, UploadContext> _uploadContexts = new();
    private readonly ConcurrentDictionary<string, DownloadContext> _downloadContexts = new();

    /// <summary>
    /// 开始向服务器上传文件
    /// </summary>
    /// <param name="localFilePath">本地上传文件路径</param>
    /// <param name="remoteFilePath">服务端保存文件路径（含文件名）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartFileUploadAsync(string localFilePath, string remoteFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法开始文件上传");
            return;
        }

        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            Logger.Error($"{ServerMark} 文件不存在：{localFilePath}");
            return;
        }

        var remoteFileName = Path.GetFileName(remoteFilePath);
        var fileHash = await ComputeFileHashAsync(localFilePath);

        var context = new UploadContext
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            LocalFilePath = localFilePath,
            RemoteFilePath = remoteFilePath,
            FileHash = fileHash,
            AlreadyTransferredBytes = 0
        };
        _uploadContexts[remoteFilePath] = context;

        var startCommand = new FileTransferStart
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            FileHash = fileHash,
            AlreadyTransferredBytes = 0,
            IsUpload = true,
            RemoteFilePath = remoteFilePath
        };

        await SendCommandAsync(startCommand);
        Logger.Info($"{ServerMark} 请求上传文件：{localFilePath} -> {remoteFilePath}，大小：{fileInfo.Length}字节，等待服务端响应...");
    }

    /// <summary>
    /// 请求从服务器下载文件
    /// </summary>
    /// <param name="serverFilePath">服务端待下载文件完整路径</param>
    /// <param name="localSaveDirectory">本地保存目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartFileDownloadAsync(string serverFilePath, string localSaveDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法开始文件下载");
            return;
        }

        var serverFileName = Path.GetFileName(serverFilePath);
        if (!Directory.Exists(localSaveDirectory))
        {
            Directory.CreateDirectory(localSaveDirectory);
        }

        var alreadyTransferredBytes = GetExistingTransferBytes(serverFilePath);
        var localSavePath = Path.Combine(localSaveDirectory, serverFileName);

        if (alreadyTransferredBytes == 0 && File.Exists(localSavePath))
        {
            alreadyTransferredBytes = new FileInfo(localSavePath).Length;
        }

        var context = new DownloadContext
        {
            FileName = serverFileName,
            FileSize = 0,
            FileHash = string.Empty,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            LocalFilePath = localSavePath,
            RemoteFilePath = serverFilePath
        };
        _downloadContexts[serverFilePath] = context;

        var startCommand = new FileTransferStart
        {
            FileName = serverFileName,
            FileSize = 0,
            FileHash = string.Empty,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            IsUpload = false,
            RemoteFilePath = serverFilePath
        };

        await SendCommandAsync(startCommand);
        Logger.Info($"{ServerMark} 请求下载文件：{serverFilePath} -> {localSaveDirectory}，已传输：{alreadyTransferredBytes}字节");
    }

    /// <summary>
    /// 处理接收到的文件块数据（用于下载：客户端接收服务器发送的文件数据）
    /// </summary>
    /// <param name="blockData">文件块数据</param>
    public async Task HandleFileBlockDataAsync(FileBlockData blockData)
    {
        var remoteFilePath = blockData.RemoteFilePath;
        if (string.IsNullOrEmpty(remoteFilePath))
        {
            Logger.Error($"{ServerMark} 文件块数据缺少RemoteFilePath");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(remoteFilePath) ?? ".";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fs = new FileStream(remoteFilePath, blockData.Offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.Write);
            await fs.WriteAsync(blockData.Data.AsMemory(0, blockData.BlockSize));

            var totalBytes = fs.Length;
            if (_downloadContexts.TryGetValue(remoteFilePath, out var context))
            {
                context.AlreadyTransferredBytes = totalBytes;
                if (context.FileSize == 0 && totalBytes > 0)
                {
                    context.FileSize = totalBytes;
                }
                var progress = context.FileSize > 0 ? (double)totalBytes / context.FileSize * 100 : 0;
                FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                    context.FileName,
                    totalBytes,
                    context.FileSize,
                    progress,
                    false));
            }

            var ack = new FileBlockAck
            {
                BlockIndex = blockData.BlockIndex,
                Success = true,
                RemoteFilePath = remoteFilePath,
                AlreadyTransferredBytes = totalBytes
            };
            await SendCommandAsync(ack);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 处理文件块({blockData.BlockIndex})异常", ex);
            var ack = new FileBlockAck
            {
                BlockIndex = blockData.BlockIndex,
                Success = false,
                Message = ex.Message,
                RemoteFilePath = remoteFilePath
            };
            await SendCommandAsync(ack);
        }
    }


    /// <summary>
    /// 发送上传文件块（内部方法，用于上传时发送文件数据到服务器）
    /// </summary>
    /// <param name="localFilePath">本地文件路径</param>
    /// <param name="remoteFilePath">远程文件路径</param>
    /// <param name="fileSize">文件大小</param>
    /// <param name="fileName">文件名</param>
    /// <param name="alreadyTransferredBytes">已传输字节数</param>
    internal async Task SendUploadBlockAsync(string localFilePath, string remoteFilePath, long fileSize, string fileName, long alreadyTransferredBytes)
    {
        if (!File.Exists(localFilePath))
        {
            Logger.Error($"{ServerMark} 文件不存在：{localFilePath}");
            return;
        }

        await using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Position = alreadyTransferredBytes;

        var blockSize = (int)Math.Min(FileTransferBlockSize, fileSize - alreadyTransferredBytes);
        if (blockSize <= 0)
        {
            return;
        }

        var buffer = new byte[blockSize];
        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, blockSize));

        if (bytesRead == 0)
        {
            return;
        }

        var blockIndex = alreadyTransferredBytes / FileTransferBlockSize;
        var blockData = new FileBlockData
        {
            BlockIndex = blockIndex,
            Offset = alreadyTransferredBytes,
            BlockSize = bytesRead,
            Data = bytesRead == blockSize ? buffer : buffer.AsSpan(0, bytesRead).ToArray(),
            RemoteFilePath = remoteFilePath
        };

        await SendCommandAsync(blockData);
        Logger.Info($"{ServerMark} 发送文件块({blockIndex})：{bytesRead}字节");

        var newTransferredBytes = alreadyTransferredBytes + bytesRead;
        var progress = fileSize > 0 ? (double)newTransferredBytes / fileSize * 100 : 0;
        FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
            fileName,
            newTransferredBytes,
            fileSize,
            progress,
            true));
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 获取已传输的字节数（用于断点续传）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>已传输的字节数</returns>
    private static long GetExistingTransferBytes(string filePath)
    {
        var progressFilePath = filePath + ".progress";
        if (File.Exists(progressFilePath))
        {
            var content = File.ReadAllText(progressFilePath);
            if (long.TryParse(content, out var bytes))
            {
                return bytes;
            }
        }
        return 0;
    }

    /// <summary>
    /// 保存传输进度到本地文件（用于断点续传）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="bytes">已传输的字节数</param>
    private static async Task SaveTransferProgressAsync(string filePath, long bytes)
    {
        var progressFilePath = filePath + ".progress";
        await File.WriteAllTextAsync(progressFilePath, bytes.ToString());
    }

    #endregion

    internal class UploadContext
    {
        public string LocalFilePath { get; set; } = string.Empty;
        public string RemoteFilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public long AlreadyTransferredBytes { get; set; }
    }

    internal class DownloadContext
    {
        public string LocalFilePath { get; set; } = string.Empty;
        public string RemoteFilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public long AlreadyTransferredBytes { get; set; }
    }
}