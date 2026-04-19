using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// TCP Socket 服务端类，用于接受 TCP 客户端连接并进行通信，支持文件传输和命令收发
/// </summary>
public class TcpSocketServer
{
    /// <summary>
    /// 客户端会话字典，键为客户端标识，值为 TCP 会话对象
    /// </summary>
    public readonly ConcurrentDictionary<string, TcpSession> Clients = new();

    private readonly Channel<(string ClientKey, SocketCommand Command)> _requests =
        Channel.CreateUnbounded<(string, SocketCommand)>();

    private PeriodicTimer? _detectionTimer;
    private CancellationTokenSource? _listenTokenSource;

    #region 公开属性

    /// <summary>
    /// 服务器 Socket 对象
    /// </summary>
    public Socket? Server { get; private set; }

    /// <summary>
    /// 系统ID，用于标识服务端身份
    /// </summary>
    public long SystemId { get; set; } = DateTime.Now.Ticks;

    /// <summary>
    /// 服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    /// 服务器IP地址
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    /// 服务器端口号
    /// </summary>
    public int ServerPort { get; private set; }

    /// <summary>
    /// 客户端超时时间（秒）
    /// </summary>
    public int TimeOut { get; private set; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    #endregion

    #region 公开接口方法

    /// <summary>
    /// 启动 TCP 服务器
    /// </summary>
    /// <param name="serverMark">服务器标识</param>
    /// <param name="serverIP">服务器IP地址</param>
    /// <param name="serverPort">服务器端口号</param>
    /// <param name="timeout">客户端超时时间（秒），默认30秒</param>
    /// <returns>启动结果和错误信息</returns>
    public async Task<(bool IsSuccess, string? ErrorMessage)> StartAsync(string serverMark, string serverIP,
        int serverPort, int timeout = 30)
    {
        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;
        TimeOut = timeout;

        try
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            Server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            Server.Bind(ipEndPoint);
            Server.Listen(10);
            IsRunning = true;
            Logger.Info($"{ServerMark} 启动成功，服务地址是：{ServerIP}:{ServerPort}");

            _listenTokenSource = new CancellationTokenSource();

            _ = Task.Run(ListenForClientsAsync);
            _ = Task.Run(ProcessingRequestsAsync);
            _ = Task.Run(DetectionClientsAsync);

            return (IsSuccess: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 启动失败，服务地址是：{ServerIP}:{ServerPort}", ex,
                uiContent: $"{ServerMark} 启动失败，服务地址是：{ServerIP}:{ServerPort}，详细日志查看日志文件");
            return (IsSuccess: false, ErrorMessage: $"{ServerMark} 启动失败，异常信息：{ex.Message}");
        }
    }

    /// <summary>
    /// 停止 TCP 服务器
    /// </summary>
    public async Task StopAsync()
    {
        IsRunning = false;
        _listenTokenSource?.Cancel();
        _detectionTimer?.Dispose();
        if (!Clients.IsEmpty)
        {
            var clientKeys = Clients.Keys.ToList();
            foreach (var clientKey in clientKeys)
            {
                await RemoveClientAsync(clientKey);
            }
        }

        Server?.Close(0);
        Server = null;
        _listenTokenSource = null;
        _detectionTimer = null;
    }

    /// <summary>
    /// 向所有已连接的客户端发送命令
    /// </summary>
    /// <param name="command">要发送的网络对象命令</param>
    public async Task SendCommandAsync(INetObject command)
    {
        if (Clients.IsEmpty)
        {
            Logger.Debug($"{ServerMark} 没有客户端上线，无发送目的地址，无法发送命令");
            return;
        }

        for (var i = Clients.Values.Count - 1; i >= 0; i--)
        {
            var client = Clients.Values.ElementAt(i);
            var clientKey = client.TcpSocket?.RemoteEndPoint?.ToString() ?? string.Empty;
            try
            {
                await SendCommandAsync(client.TcpSocket, command);
            }
            catch (SocketException ex)
            {
                Logger.Error($"{ServerMark} 发送命令到客户端({clientKey})异常，将移除该客户端", ex,
                    uiContent: $"{ServerMark} 发送命令到客户端({clientKey})异常，将移除该客户端，详细信息请查看日志文件");
                await RemoveClientAsync(clientKey);
            }
        }
    }

    /// <summary>
    /// 向指定客户端发送命令
    /// </summary>
    /// <param name="client">客户端 Socket 对象</param>
    /// <param name="command">要发送的网络对象命令</param>
    public async Task SendCommandAsync(Socket client, INetObject command)
    {
        var buffer = command.Serialize(SystemId);
        await client.SendAsync(buffer);
    }

    #endregion

    #region 接收客户端命令

    /// <summary>
    /// 移除客户端连接（内部方法，通过 Socket 对象）
    /// </summary>
    /// <param name="tcpClient">客户端 Socket 对象</param>
    private async Task RemoveClientAsync(Socket tcpClient)
    {
        await RemoveClientAsync(tcpClient.RemoteEndPoint!.ToString()!);
    }

    /// <summary>
    /// 移除客户端连接（内部方法，通过客户端键）
    /// </summary>
    /// <param name="key">客户端标识键</param>
    private async Task RemoveClientAsync(string key)
    {
        if (!Clients.TryGetValue(key, out var session))
        {
            return;
        }

        session.TokenSource?.Cancel();
        session.TcpSocket?.Close();

        Clients.TryRemove(key, out _);

        await EventBus.EventBus.Default.PublishAsync(new SocketClientChangedCommand(this));
        Logger.Warn($"{ServerMark} 已清除客户端信息{key}");
    }

    /// <summary>
    /// 监听客户端连接请求（内部方法）
    /// </summary>
    private async Task ListenForClientsAsync()
    {
        while (IsRunning && _listenTokenSource?.IsCancellationRequested != true && Server != null)
        {
            try
            {
                var socketClient = await Server.AcceptAsync();
                var session = await CacheClientAsync(socketClient);
                await EventBus.EventBus.Default.PublishAsync(new SocketClientChangedCommand(this));

                var socketClientKey = $"{socketClient.RemoteEndPoint}";

                Logger.Info($"{ServerMark} 客户端({socketClientKey})连接上线");

                _ = Task.Run(async () => await HandleClientAsync(session));
            }
            catch (Exception ex)
            {
                if (IsRunning)
                {
                    Logger.Error($"{ServerMark} 处理客户端连接上线异常", ex, uiContent: $"{ServerMark} 处理客户端连接上线异常，详细信息请查看日志文件");
                }
            }
        }
    }

    /// <summary>
    /// 处理客户端数据接收（内部方法）
    /// </summary>
    /// <param name="client">TCP 会话对象</param>
    private async Task HandleClientAsync(TcpSession client)
    {
        var tcpClientKey = client.TcpSocket?.RemoteEndPoint?.ToString() ?? string.Empty;
        while (IsRunning && _listenTokenSource?.IsCancellationRequested != true &&
               client.TokenSource?.IsCancellationRequested != true)
        {
            try
            {
                var (success, buffer, headInfo) = await client.TcpSocket!.ReadPacketAsync();
                if (!success)
                {
                    break;
                }

                await _requests.Writer.WriteAsync((tcpClientKey, new SocketCommand(headInfo!, buffer, client.TcpSocket)));
            }
            catch (SocketException ex)
            {
                Logger.Error($"{ServerMark} 远程主机({tcpClientKey})异常，将移除该客户端", ex,
                    uiContent: $"{ServerMark} 远程主机({tcpClientKey})异常，将移除该客户端，详细信息请查看日志文件");
                await RemoveClientAsync(tcpClientKey);
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 接收数据异常", ex, uiContent: $"{ServerMark} 接收数据异常，详细信息请查看日志文件");
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    #endregion

    #region 处理客户端请求

    /// <summary>
    /// 处理客户端请求（内部方法）
    /// </summary>
    private async Task ProcessingRequestsAsync()
    {
        await foreach (var (clientKey, command) in _requests.Reader.ReadAllAsync())
        {
            if (!Clients.TryGetValue(clientKey, out var client))
            {
                continue;
            }

            try
            {
                ActiveClient(clientKey);
                await EventBus.EventBus.Default.PublishAsync(command);
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 处理客户端请求异常", ex,
                    uiContent: $"{ServerMark} 处理客户端请求异常，详细信息请查看日志文件");
            }
        }
    }

    /// <summary>
    /// 缓存客户端会话（内部方法）
    /// </summary>
    /// <param name="socket">客户端 Socket 对象</param>
    /// <returns>TCP 会话对象</returns>
    private async Task<TcpSession> CacheClientAsync(Socket? socket)
    {
        if (socket == null)
        {
            throw new ArgumentNullException(nameof(socket));
        }

        var key = socket.RemoteEndPoint?.ToString() ?? string.Empty;
        if (Clients.ContainsKey(key))
        {
            await RemoveClientAsync(key);
        }

        var session = new TcpSession
        {
            TcpSocket = socket,
            TokenSource = new CancellationTokenSource(),
            ActiveTime = DateTime.Now
        };
        Clients.TryAdd(key, session);
        return session;
    }

    /// <summary>
    /// 定时检测客户端连接状态（内部方法）
    /// </summary>
    private async Task DetectionClientsAsync()
    {
        if (_listenTokenSource == null)
            return;

        _detectionTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await _detectionTimer.WaitForNextTickAsync(_listenTokenSource.Token))
            {
                var clientKeys = Clients.Keys;
                foreach (var clientKey in clientKeys)
                {
                    if (!Clients.TryGetValue(clientKey, out var clientSession))
                    {
                        continue;
                    }

                    if (!clientSession.ActiveTime.HasValue ||
                        DateTime.Now.Subtract(clientSession.ActiveTime.Value).TotalSeconds > TimeOut)
                    {
                        await RemoveClientAsync(clientKey);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 心跳检测异常", ex, uiContent: $"{ServerMark} 客户端心跳检测异常，详细信息请查看日志文件");
        }
    }

    /// <summary>
    /// 更新客户端最后活动时间（内部方法）
    /// </summary>
    /// <param name="clientKey">客户端标识键</param>
    private void ActiveClient(string clientKey)
    {
        if (Clients.TryGetValue(clientKey, out var session))
        {
            session.ActiveTime = DateTime.Now;
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

    private readonly ConcurrentDictionary<string, FileTransferSession> _fileTransferSessions = new();

    /// <summary>
    /// 开始向客户端上传文件（服务器作为文件源）
    /// </summary>
    /// <param name="clientKey">客户端标识键</param>
    /// <param name="localFilePath">本地文件路径</param>
    /// <param name="remoteFileName">远程文件名</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartFileUploadAsync(string clientKey, string localFilePath, string remoteFileName,
        CancellationToken cancellationToken = default)
    {
        if (!Clients.TryGetValue(clientKey, out var session) || session.TcpSocket == null)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})不存在，无法开始文件上传");
            return;
        }

        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            Logger.Error($"{ServerMark} 文件不存在：{localFilePath}");
            return;
        }

        var transferSession = new FileTransferSession
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            FileHash = await ComputeFileHashAsync(localFilePath),
            LocalFilePath = localFilePath,
            IsUpload = true
        };

        transferSession.AlreadyTransferredBytes = GetExistingTransferBytes(remoteFileName, transferSession.FileHash);
        _fileTransferSessions[clientKey] = transferSession;

        var startCommand = new FileTransferStart
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            FileHash = transferSession.FileHash,
            AlreadyTransferredBytes = transferSession.AlreadyTransferredBytes,
            IsUpload = true
        };

        await SendCommandAsync(session.TcpSocket, startCommand);
        Logger.Info($"{ServerMark} 向客户端({clientKey})发送文件传输请求：{remoteFileName}，已传输：{transferSession.AlreadyTransferredBytes}字节");
    }

    /// <summary>
    /// 请求从客户端下载文件（服务器作为文件目标）
    /// </summary>
    /// <param name="clientKey">客户端标识键</param>
    /// <param name="saveDirectory">保存目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartFileDownloadAsync(string clientKey, string saveDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Clients.TryGetValue(clientKey, out var session) || session.TcpSocket == null)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})不存在，无法开始文件下载");
            return;
        }

        if (!Directory.Exists(saveDirectory))
        {
            Directory.CreateDirectory(saveDirectory);
        }
    }

    /// <summary>
    /// 处理接收到的文件块数据（用于下载：服务器接收客户端发送的文件数据）
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="blockIndex">块索引号</param>
    /// <param name="offset">数据偏移量</param>
    /// <param name="blockSize">数据块大小</param>
    /// <param name="data">数据内容</param>
    public async Task HandleFileBlockDataAsync(Socket client, long blockIndex, long offset, int blockSize, byte[] data)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        if (!_fileTransferSessions.TryGetValue(clientKey, out var session))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(session.LocalFilePath) ?? ".";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fs = new FileStream(session.LocalFilePath, FileMode.Append, FileAccess.Write, FileShare.Write);
            await fs.WriteAsync(data.AsMemory(0, blockSize));

            var ack = new FileBlockAck
            {
                BlockIndex = blockIndex,
                Success = true
            };
            await SendCommandAsync(client, ack);

            session.AlreadyTransferredBytes += blockSize;
            var progress = (double)session.AlreadyTransferredBytes / session.FileSize * 100;
            FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(session.FileName, session.AlreadyTransferredBytes, session.FileSize, progress, session.IsUpload));

            if (session.AlreadyTransferredBytes >= session.FileSize)
            {
                await HandleFileTransferCompleteAsync(client, session);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 处理文件块({blockIndex})异常", ex);
            var ack = new FileBlockAck
            {
                BlockIndex = blockIndex,
                Success = false,
                Message = ex.Message
            };
            await SendCommandAsync(client, ack);
        }
    }

    /// <summary>
    /// 处理客户端发起的文件传输开始请求（用于下载：服务器作为接收端，客户端作为发送端）
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="fileName">文件名</param>
    /// <param name="fileSize">文件大小</param>
    /// <param name="fileHash">文件哈希值</param>
    /// <param name="alreadyTransferredBytes">已传输字节数（断点续传用）</param>
    public async Task HandleFileTransferStartAsync(Socket client, string fileName, long fileSize, string fileHash, long alreadyTransferredBytes)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;

        var savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
        var session = new FileTransferSession
        {
            FileName = fileName,
            FileSize = fileSize,
            FileHash = fileHash,
            LocalFilePath = savePath,
            IsUpload = false,
            AlreadyTransferredBytes = alreadyTransferredBytes
        };

        _fileTransferSessions[clientKey] = session;

        var ack = new FileTransferStartAck
        {
            Accept = true,
            AlreadyTransferredBytes = alreadyTransferredBytes
        };
        await SendCommandAsync(client, ack);
        Logger.Info($"{ServerMark} 收到客户端({clientKey})下载请求：{fileName}，已传输：{alreadyTransferredBytes}字节");

        await SendNextBlockAsync(clientKey);
    }

    /// <summary>
    /// 处理文件块传输应答（用于上传：服务器发送文件块后等待客户端确认）
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="blockIndex">块索引号</param>
    /// <param name="success">是否成功</param>
    /// <param name="message">消息内容</param>
    public async Task HandleFileBlockAckAsync(Socket client, long blockIndex, bool success, string message)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        if (!_fileTransferSessions.TryGetValue(clientKey, out var session))
        {
            return;
        }

        if (!success)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})报告文件块传输失败：{message}");
            return;
        }

        if (session.IsUpload)
        {
            await SendNextBlockAsync(clientKey);
        }
    }

    /// <summary>
    /// 发送下一个文件块（内部方法，用于上传时发送文件数据）
    /// </summary>
    /// <param name="clientKey">客户端标识键</param>
    internal async Task SendNextBlockAsync(string clientKey)
    {
        if (!Clients.TryGetValue(clientKey, out var session) || session.TcpSocket == null)
        {
            return;
        }

        if (!_fileTransferSessions.TryGetValue(clientKey, out var transferSession) || !transferSession.IsUpload)
        {
            return;
        }

        var fileInfo = new FileInfo(transferSession.LocalFilePath);
        if (!fileInfo.Exists)
        {
            Logger.Error($"{ServerMark} 文件不存在：{transferSession.LocalFilePath}");
            return;
        }

        using var fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Position = transferSession.AlreadyTransferredBytes;

        var blockSize = (int)Math.Min(FileTransferBlockSize, transferSession.FileSize - transferSession.AlreadyTransferredBytes);
        var buffer = new byte[blockSize];
        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, blockSize));

        if (bytesRead == 0)
        {
            return;
        }

        var blockIndex = transferSession.AlreadyTransferredBytes / FileTransferBlockSize;
        var blockData = new FileBlockData
        {
            BlockIndex = blockIndex,
            Offset = transferSession.AlreadyTransferredBytes,
            BlockSize = bytesRead,
            Data = bytesRead == blockSize ? buffer : buffer.AsSpan(0, bytesRead).ToArray()
        };

        await SendCommandAsync(session.TcpSocket, blockData);

        transferSession.AlreadyTransferredBytes += bytesRead;
        var progress = (double)transferSession.AlreadyTransferredBytes / transferSession.FileSize * 100;
        FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(transferSession.FileName, transferSession.AlreadyTransferredBytes, transferSession.FileSize, progress, transferSession.IsUpload));

        if (transferSession.AlreadyTransferredBytes >= transferSession.FileSize)
        {
            var completeCommand = new FileTransferComplete
            {
                FileHash = transferSession.FileHash,
                Success = true
            };
            await SendCommandAsync(session.TcpSocket, completeCommand);
            _fileTransferSessions.TryRemove(clientKey, out _);
            SaveTransferProgress(transferSession.FileName, transferSession.FileHash, transferSession.FileSize);
            Logger.Info($"{ServerMark} 文件上传完成：{transferSession.FileName}");
        }
    }

    /// <summary>
    /// 处理文件传输完成（内部方法）
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="session">文件传输会话</param>
    private async Task HandleFileTransferCompleteAsync(Socket client, FileTransferSession session)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        if (session.IsUpload)
        {
            var completeCommand = new FileTransferComplete
            {
                FileHash = session.FileHash,
                Success = true
            };
            await SendCommandAsync(client, completeCommand);
        }
        _fileTransferSessions.TryRemove(clientKey, out _);
        SaveTransferProgress(session.FileName, session.FileHash, session.FileSize);
        Logger.Info($"{ServerMark} 文件传输完成：{session.FileName}");
    }

    /// <summary>
    /// 将文件块写入文件（内部方法）
    /// </summary>
    /// <param name="session">文件传输会话</param>
    /// <param name="block">文件块数据</param>
    private async Task WriteBlockToFileAsync(FileTransferSession session, FileBlockData block)
    {
        var directory = Path.GetDirectoryName(session.LocalFilePath) ?? ".";
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fs = new FileStream(session.LocalFilePath, block.Offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None);
        await fs.WriteAsync(block.Data.AsMemory(0, block.BlockSize));
    }

    /// <summary>
    /// 计算文件的SHA256哈希值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>十六进制哈希字符串</returns>
    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 获取已存在的传输进度（用于断点续传）
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileHash">文件哈希</param>
    /// <returns>已传输的字节数</returns>
    private long GetExistingTransferBytes(string fileName, string fileHash)
    {
        var progressFile = GetProgressFilePath(fileName, fileHash);
        if (File.Exists(progressFile))
        {
            var content = File.ReadAllText(progressFile);
            if (long.TryParse(content.Trim(), out var bytes))
            {
                return bytes;
            }
        }
        return 0;
    }

    /// <summary>
    /// 保存传输进度（用于断点续传）
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileHash">文件哈希</param>
    /// <param name="totalBytes">总字节数</param>
    private void SaveTransferProgress(string fileName, string fileHash, long totalBytes)
    {
        var progressFile = GetProgressFilePath(fileName, fileHash);
        File.WriteAllText(progressFile, totalBytes.ToString());
    }

    /// <summary>
    /// 获取传输进度文件路径
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileHash">文件哈希</param>
    /// <returns>进度文件路径</returns>
    private static string GetProgressFilePath(string fileName, string fileHash) =>
        Path.Combine(Path.GetTempPath(), $"file_transfer_server_{fileName}_{fileHash}.progress");

    #endregion
}