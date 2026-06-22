namespace CodeWF.NetWrapper.Helpers;

/// <summary>
///     TCP Socket 服务端类，用于接受 TCP 客户端连接并进行通信，支持文件传输和命令收发
/// </summary>
public partial class TcpSocketServer
{
    /// <summary>
    ///     客户端会话字典，键为客户端标识，值为 TCP 会话对象
    /// </summary>
    public readonly ConcurrentDictionary<string, TcpSession> Clients = new();

    private PeriodicTimer? _detectionTimer;
    private CancellationTokenSource? _listenTokenSource;

    private Channel<(string ClientKey, SocketCommand Command)> _requests =
        Channel.CreateUnbounded<(string, SocketCommand)>();

    #region 公开属性

    /// <summary>
    ///     服务器 Socket 对象
    /// </summary>
    public Socket? Server { get; private set; }

    /// <summary>
    ///     系统ID，用于标识服务端身份
    /// </summary>
    public long SystemId { get; set; } = DateTime.Now.Ticks;

    /// <summary>
    ///     服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    ///     服务器IP地址
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    ///     服务器端口号
    /// </summary>
    public int ServerPort { get; private set; }

    /// <summary>
    ///     客户端超时时间（秒）
    /// </summary>
    public int TimeOut { get; private set; }

    /// <summary>
    ///     是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    #endregion

    #region 公开接口方法

    /// <summary>
    ///     启动 TCP 服务器
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
            var (requests, fileTransferRequests) = ResetRequestChannels();

            _ = Task.Run(async () => await ListenForClientsAsync(_listenTokenSource, requests.Writer));
            _ = Task.Run(async () => await ProcessingRequestsAsync(requests.Reader, fileTransferRequests.Writer));
            _ = Task.Run(async () => await ProcessingFileTransferRequestsAsync(fileTransferRequests.Reader));
            _ = Task.Run(async () => await DetectionClientsAsync(_listenTokenSource));

            return (IsSuccess: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 启动失败，服务地址是：{ServerIP}:{ServerPort}", ex,
                $"{ServerMark} 启动失败，服务地址是：{ServerIP}:{ServerPort}，详细日志查看日志文件");
            return (IsSuccess: false, ErrorMessage: $"{ServerMark} 启动失败，异常信息：{ex.Message}");
        }
    }

    /// <summary>
    ///     停止 TCP 服务器
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

        CompleteRequestChannels();
        _uploadContexts.Clear();
        _downloadContexts.Clear();
        Server?.Close(0);
        Server = null;
        _listenTokenSource = null;
        _detectionTimer = null;
    }

    /// <summary>
    ///     向所有已连接的客户端发送命令
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
            var socket = client.TcpSocket;
            if (socket == null)
            {
                await RemoveClientAsync(clientKey);
                continue;
            }

            try
            {
                await SendCommandAsync(socket, command);
            }
            catch (SocketException ex)
            {
                Logger.Error($"{ServerMark} 发送命令到客户端({clientKey})异常，将移除该客户端", ex,
                    $"{ServerMark} 发送命令到客户端({clientKey})异常，将移除该客户端，详细信息请查看日志文件");
                await RemoveClientAsync(clientKey);
            }
        }
    }

    /// <summary>
    ///     向指定客户端发送命令
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
    ///     移除客户端连接（内部方法，通过 Socket 对象）
    /// </summary>
    /// <param name="tcpClient">客户端 Socket 对象</param>
    private async Task RemoveClientAsync(Socket tcpClient)
    {
        await RemoveClientAsync(tcpClient.RemoteEndPoint!.ToString()!);
    }

    /// <summary>
    ///     移除客户端连接（内部方法，通过客户端键）
    /// </summary>
    /// <param name="key">客户端标识键</param>
    private async Task RemoveClientAsync(string key)
    {
        if (!Clients.TryGetValue(key, out var session))
        {
            return;
        }

        session.TokenSource?.Cancel();
        session.TcpSocket?.CloseSocket();

        Clients.TryRemove(key, out _);

        await EventBus.EventBus.Default.PublishAsync(new SocketClientChangedCommand(this));
        Logger.Warn($"{ServerMark} 已清除客户端信息{key}");
    }

    /// <summary>
    ///     监听客户端连接请求（内部方法）
    /// </summary>
    private async Task ListenForClientsAsync(CancellationTokenSource listenTokenSource,
        ChannelWriter<(string ClientKey, SocketCommand Command)> requests)
    {
        var server = Server;
        while (IsRunning && ReferenceEquals(_listenTokenSource, listenTokenSource) &&
               !listenTokenSource.IsCancellationRequested && server != null)
        {
            try
            {
                var socketClient = await server.AcceptAsync();
                var session = await CacheClientAsync(socketClient);
                await EventBus.EventBus.Default.PublishAsync(new SocketClientChangedCommand(this));

                var socketClientKey = $"{socketClient.RemoteEndPoint}";

                Logger.Info($"{ServerMark} 客户端({socketClientKey})连接上线");

                _ = Task.Run(async () => await HandleClientAsync(session, listenTokenSource, requests));
            }
            catch (Exception ex)
            {
                if (IsRunning && ReferenceEquals(_listenTokenSource, listenTokenSource))
                {
                    Logger.Error($"{ServerMark} 处理客户端连接上线异常", ex, $"{ServerMark} 处理客户端连接上线异常，详细信息请查看日志文件");
                }
            }
        }
    }

    /// <summary>
    ///     处理客户端数据接收（内部方法）
    /// </summary>
    /// <param name="client">TCP 会话对象</param>
    private async Task HandleClientAsync(TcpSession client, CancellationTokenSource listenTokenSource,
        ChannelWriter<(string ClientKey, SocketCommand Command)> requests)
    {
        var tcpClientKey = client.TcpSocket?.RemoteEndPoint?.ToString() ?? string.Empty;
        using var receiveTokenSource = client.TokenSource == null
            ? CancellationTokenSource.CreateLinkedTokenSource(listenTokenSource.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(listenTokenSource.Token, client.TokenSource.Token);

        while (IsRunning && ReferenceEquals(_listenTokenSource, listenTokenSource) &&
               !listenTokenSource.IsCancellationRequested &&
               client.TokenSource?.IsCancellationRequested != true)
        {
            try
            {
                var (success, buffer, headInfo) = await client.TcpSocket!.ReadPacketAsync(receiveTokenSource.Token);
                if (!success || buffer == null || headInfo == null)
                {
                    Logger.Warn($"{ServerMark} 客户端({tcpClientKey})连接已断开，将移除该客户端");
                    await RemoveClientAsync(tcpClientKey);
                    break;
                }

                await requests.WriteAsync((tcpClientKey, new SocketCommand(headInfo, buffer, client.TcpSocket)));
            }
            catch (OperationCanceledException) when (IsClientReceiveStopped(client, listenTokenSource))
            {
                break;
            }
            catch (SocketException ex) when (IsClientReceiveStopped(client, listenTokenSource) ||
                                             IsSocketOperationAborted(ex))
            {
                Logger.Debug($"{ServerMark} 客户端({tcpClientKey})接收已取消，结束接收循环");
                break;
            }
            catch (SocketException ex)
            {
                Logger.Error($"{ServerMark} 远程主机({tcpClientKey})异常，将移除该客户端", ex,
                    $"{ServerMark} 远程主机({tcpClientKey})异常，将移除该客户端，详细信息请查看日志文件");
                await RemoveClientAsync(tcpClientKey);
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 接收数据异常", ex, $"{ServerMark} 接收数据异常，详细信息请查看日志文件");
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private bool IsClientReceiveStopped(TcpSession client, CancellationTokenSource listenTokenSource)
    {
        return !IsRunning
               || !ReferenceEquals(_listenTokenSource, listenTokenSource)
               || listenTokenSource.IsCancellationRequested
               || client.TokenSource?.IsCancellationRequested == true;
    }

    private static bool IsSocketOperationAborted(SocketException ex)
    {
        return ex.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted;
    }

    #endregion

    #region 处理客户端请求

    /// <summary>
    ///     处理客户端请求（内部方法）
    /// </summary>
    private async Task ProcessingRequestsAsync(ChannelReader<(string ClientKey, SocketCommand Command)> requests,
        ChannelWriter<(string ClientKey, SocketCommand Command)> fileTransferRequests)
    {
        await foreach (var (clientKey, command) in requests.ReadAllAsync())
        {
            if (!Clients.TryGetValue(clientKey, out var client))
            {
                continue;
            }

            try
            {
                ActiveClient(clientKey);

                if (command.IsCommand<FileUploadRequest>() ||
                    command.IsCommand<FileDownloadRequest>() ||
                    command.IsCommand<FileChunkData>() ||
                    command.IsCommand<FileChunkAck>() ||
                    command.IsCommand<FileTransferReject>() ||
                    command.IsCommand<BrowseFileSystemRequest>() ||
                    command.IsCommand<CreateDirectoryRequest>() ||
                    command.IsCommand<DeletePathRequest>())
                {
                    await fileTransferRequests.WriteAsync((clientKey, command));
                }
                else
                {
                    await EventBus.EventBus.Default.PublishAsync(command);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 处理客户端请求异常", ex,
                    $"{ServerMark} 处理客户端请求异常，详细信息请查看日志文件");
            }
        }
    }

    private (Channel<(string ClientKey, SocketCommand Command)> Requests,
        Channel<(string ClientKey, SocketCommand Command)> FileTransferRequests) ResetRequestChannels()
    {
        _requests = Channel.CreateUnbounded<(string, SocketCommand)>();
        _fileTransferRequests = Channel.CreateUnbounded<(string, SocketCommand)>();
        return (_requests, _fileTransferRequests);
    }

    private void CompleteRequestChannels()
    {
        _requests.Writer.TryComplete();
        _fileTransferRequests.Writer.TryComplete();
    }

    /// <summary>
    ///     缓存客户端会话（内部方法）
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
    ///     定时检测客户端连接状态（内部方法）
    /// </summary>
    private async Task DetectionClientsAsync(CancellationTokenSource listenTokenSource)
    {
        if (!ReferenceEquals(_listenTokenSource, listenTokenSource))
        {
            return;
        }

        _detectionTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (ReferenceEquals(_listenTokenSource, listenTokenSource) &&
                   await _detectionTimer.WaitForNextTickAsync(listenTokenSource.Token))
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
            Logger.Error($"{ServerMark} 心跳检测异常", ex, $"{ServerMark} 客户端心跳检测异常，详细信息请查看日志文件");
        }
    }

    /// <summary>
    ///     更新客户端最后活动时间（内部方法）
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
}
