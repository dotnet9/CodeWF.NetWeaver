namespace CodeWF.NetWrapper.Helpers;

/// <summary>
///     TCP Socket 客户端类，用于与 TCP 服务器建立连接并进行通信。
/// </summary>
public partial class TcpSocketClient
{
    private Socket? _client;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Func<SocketCommand, Task<bool>>> _commandHandlers = new();
    private const int MaxPendingResponses = 4096;
    private Task? _listenTask;
    private Task? _responseTask;
    private Channel<SocketCommand> _responses = CreateResponseChannel();

    /// <summary>
    ///     标记当前执行流正处于本客户端派发给订阅者的回调上下文中（含其异步延续）。
    ///     用于 Stop()/StopAsync() 重入防御：回调里再调用 Stop() 时不能阻塞等待内部线程退出。
    /// </summary>
    private static readonly AsyncLocal<TcpSocketClient?> CallbackClient = new();

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
        if (IsRunning || _client != null)
        {
            await StopAsync();
        }

        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;

        try
        {
            var responses = ResetResponseChannels();

            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _client.ConnectAsync(ipEndPoint);

            IsRunning = true;
            Logger.Info($"{ServerMark} 连接成功，服务地址是： {ServerIP}:{ServerPort}，当前客户端地址：{_client.LocalEndPoint}");

            _listenTask = ListenForServerAsync(responses.Writer);
            _responseTask = CheckResponseAsync(responses.Reader);

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
        StopAsync().GetAwaiter().GetResult();
    }

    public async Task StopAsync()
    {
        // 重入防御：订阅者回调里调用 Stop() 时，当前执行流正处于库派发事件的回调上下文，
        // 阻塞等待内部线程退出会形成自己等自己的死锁；改为设置退出标记后由内部线程自行收尾。
        var reentrant = ReferenceEquals(CallbackClient.Value, this);

        CloseConnection();
        CompleteResponseChannels();

        var listenTask = _listenTask;
        var responseTask = _responseTask;
        _listenTask = null;
        _responseTask = null;
        if (reentrant)
        {
            return;
        }

        if (listenTask != null)
        {
            await IgnoreBackgroundTaskAsync(listenTask);
        }

        if (responseTask != null)
        {
            await IgnoreBackgroundTaskAsync(responseTask);
        }
    }

    /// <summary>
    ///     发送命令到服务器
    /// </summary>
    /// <param name="command">要发送的网络对象命令</param>
    /// <exception cref="Exception">未连接时抛出异常</exception>
    public async Task SendCommandAsync(INetObject command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var netObjInfo = command.GetType().GetNetObjectHead();
        if (!CanSend)
        {
            throw new Exception($"{ServerMark} 未连接，无法发送命令【ID：{netObjInfo.Id}，Version：{netObjInfo.Version}】");
        }

        var buffer = command.Serialize(SystemId);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _client!.SendExactAsync(buffer, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    ///     注册客户端收到命令后的扩展处理器。处理器返回 true 表示命令已被消费。
    /// </summary>
    public IDisposable RegisterCommandHandler(Func<SocketCommand, Task<bool>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        _commandHandlers[id] = handler;
        return new CommandHandlerRegistration(() => _commandHandlers.TryRemove(id, out _));
    }

    #endregion

    #region 连接TCP、接收数据

    /// <summary>
    ///     监听服务器消息（内部方法）
    /// </summary>
    private async Task ListenForServerAsync(ChannelWriter<SocketCommand> responses)
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
                    HandleServerReceiveStopped(socket, responses, localEndPoint);
                    break;
                }

                SystemId = headInfo.SystemId;
                await responses.WriteAsync(new SocketCommand(headInfo, buffer, socket));
            }
            catch (Exception ex)
            {
                HandleServerReceiveStopped(socket, responses, localEndPoint, ex);
                break;
            }
        }
    }

    private void HandleServerReceiveStopped(Socket? socket, ChannelWriter<SocketCommand> responses,
        string? localEndPoint, Exception? exception = null)
    {
        var shouldPublish = IsRunning && ReferenceEquals(_client, socket);
        var msg = exception == null
            ? $"{ServerMark} 服务端连接已断开，当前客户端地址：{localEndPoint}"
            : $"{ServerMark} 处理接收数据异常，当前客户端地址：{localEndPoint}";

        CloseConnection(socket);
        CompleteResponseChannels(responses);

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

        DispatchToSubscribers(
            () => EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(this, msg)),
            $"{ServerMark} 发布断开事件异常");
    }

    /// <summary>
    ///     把对外事件派发移出内部接收/回调线程（线程池异步执行），避免订阅者在回调里
    ///     调用 Stop() 时等待内部线程退出形成自死锁。事件参数均为不可变快照；
    ///     处理器异常在此兜底隔离，不影响内部接收循环与库本身。
    /// </summary>
    private void DispatchToSubscribers(Func<Task> raise, string errorTitle)
    {
        var client = this;
        _ = Task.Run(async () =>
        {
            var previous = CallbackClient.Value;
            CallbackClient.Value = client;
            try
            {
                await raise();
            }
            catch (Exception ex)
            {
                Logger.Error(errorTitle, ex, $"{errorTitle}，详细信息请查看日志文件");
            }
            finally
            {
                CallbackClient.Value = previous;
            }
        });
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

    private Channel<SocketCommand> ResetResponseChannels()
    {
        _responses = CreateResponseChannel();
        return _responses;
    }

    private static Channel<SocketCommand> CreateResponseChannel() => Channel.CreateBounded<SocketCommand>(
        new BoundedChannelOptions(MaxPendingResponses)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

    private static async Task IgnoreBackgroundTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("TCP 客户端后台任务异常", ex, "TCP 客户端后台任务异常，详细信息请查看日志文件");
        }
    }

    private void CompleteResponseChannels()
    {
        CompleteResponseChannels(_responses.Writer);
    }

    private static void CompleteResponseChannels(ChannelWriter<SocketCommand> responses)
    {
        responses.TryComplete();
    }

    /// <summary>
    ///     检查响应队列并发布事件（内部方法）
    /// </summary>
    private async Task CheckResponseAsync(ChannelReader<SocketCommand> responses)
    {
        await foreach (var command in responses.ReadAllAsync())
        {
            try
            {
                if (await TryHandleCommandAsync(command))
                {
                    continue;
                }

                DispatchToSubscribers(
                    () => EventBus.EventBus.Default.PublishAsync(command),
                    $"{ServerMark} 发布命令事件异常");
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 处理服务端命令异常", ex,
                    $"{ServerMark} 处理服务端命令异常，详细信息请查看日志文件");
            }
        }
    }

    private async Task<bool> TryHandleCommandAsync(SocketCommand command)
    {
        foreach (var handler in _commandHandlers.Values)
        {
            // 命令处理器在内部消费线程上同步执行（需返回是否已消费），标记回调上下文供 Stop() 重入防御；
            // 逐处理器隔离异常，单个处理器异常不影响其他处理器和消费循环。
            var previous = CallbackClient.Value;
            CallbackClient.Value = this;
            try
            {
                if (await handler(command))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 命令处理器异常", ex,
                    $"{ServerMark} 命令处理器异常，详细信息请查看日志文件");
            }
            finally
            {
                CallbackClient.Value = previous;
            }
        }

        return false;
    }

    #endregion
}
