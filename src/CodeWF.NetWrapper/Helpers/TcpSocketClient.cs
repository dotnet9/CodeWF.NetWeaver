namespace CodeWF.NetWrapper.Helpers;

/// <summary>
///     TCP Socket 客户端类，用于与 TCP 服务器建立连接并进行通信。
/// </summary>
public partial class TcpSocketClient
{
    private Socket? _client;
    private readonly ConcurrentDictionary<Guid, Func<SocketCommand, Task<bool>>> _commandHandlers = new();
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
            var responses = ResetResponseChannels();

            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _client.ConnectAsync(ipEndPoint);

            IsRunning = true;
            Logger.Info($"{ServerMark} 连接成功，服务地址是： {ServerIP}:{ServerPort}，当前客户端地址：{_client.LocalEndPoint}");

            _ = Task.Run(async () => await ListenForServerAsync(responses.Writer));
            _ = Task.Run(async () => await CheckResponseAsync(responses.Reader));

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
                    await HandleServerReceiveStoppedAsync(socket, responses, localEndPoint);
                    break;
                }

                SystemId = headInfo.SystemId;
                await responses.WriteAsync(new SocketCommand(headInfo, buffer, socket));
            }
            catch (Exception ex)
            {
                await HandleServerReceiveStoppedAsync(socket, responses, localEndPoint, ex);
                break;
            }
        }
    }

    private async Task HandleServerReceiveStoppedAsync(Socket? socket, ChannelWriter<SocketCommand> responses,
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

    private Channel<SocketCommand> ResetResponseChannels()
    {
        _responses = Channel.CreateUnbounded<SocketCommand>();
        return _responses;
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
            if (await TryHandleCommandAsync(command))
            {
                continue;
            }

            await EventBus.EventBus.Default.PublishAsync(command);
        }
    }

    private async Task<bool> TryHandleCommandAsync(SocketCommand command)
    {
        foreach (var handler in _commandHandlers.Values)
        {
            if (await handler(command))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
