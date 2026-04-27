using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using CodeWF.Tools.Helpers;
using ReactiveUI;
using SocketDto;
using SocketDto.AutoCommand;
using SocketDto.Enums;
using SocketDto.Requests;
using SocketDto.Response;
using SocketDto.Udp;
using SocketTest.Server.Configuration;
using SocketTest.Server.Features.Processes.Services;
using SocketTest.Server.Shell.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Notification = Avalonia.Controls.Notifications.Notification;
using Timer = System.Timers.Timer;

namespace SocketTest.Server.Shell.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private const int ProcessPageSize = 500;
    private const int SnapshotRefreshMilliseconds = 1000;
    private const int RealtimeUdpMilliseconds = 500;
    private const int GeneralUdpMilliseconds = 1000;
    private const int ProcessStructureChangeDebounceMilliseconds = 1000;

    private const string TcpIpKey = "TcpIp";
    private const string TcpPortKey = "TcpPort";
    private const string UdpIpKey = "UdpIp";
    private const string UdpPortKey = "UdpPort";

    private readonly IProcessSnapshotProvider _processSnapshotProvider;
    private readonly object _processStructureChangeDebounceSyncRoot = new();
    private readonly string _settingsFilePath;
    private readonly ServerRuntimeSettings _runtimeSettings = new();
    private Task? _initialSnapshotWarmupTask;
    private CancellationTokenSource? _processStructureChangeDebounceCts;
    private Timer? _snapshotRefreshTimer;
    private Timer? _sendRealtimeDataTimer;
    private Timer? _sendGeneralDataTimer;

    internal MainWindowViewModel(IProcessSnapshotProvider processSnapshotProvider)
    {
        _processSnapshotProvider = processSnapshotProvider;
        _settingsFilePath = AppContext.GetData("APP_CONFIG_FILE") as string
            ?? "应用配置文件";

        TcpHelper = new TcpSocketServer();
        UdpHelper = new UdpSocketServer();
        ConnectedClients.CollectionChanged += ConnectedClientsOnCollectionChanged;

        EventBus.Default.Subscribe(this);

        Logger.Info($"服务端配置文件：{_settingsFilePath}");
        Logger.Info("首次启动时会自动生成 TCP/UDP 配置并写入配置文件。");
        PublishServerStatusChanged();
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public TcpSocketServer TcpHelper { get; }

    public UdpSocketServer UdpHelper { get; }

    public ObservableCollection<KeyValuePair<string, Socket>> ConnectedClients { get; } = [];

    public bool IsRunning { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public int CurrentProcessCount { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public int ClientCount => ConnectedClients.Count;

    public string StartButtonText => IsRunning ? "停止服务" : "启动服务";

    public string ServiceStatusText => IsRunning ? "服务运行中" : "服务未启动";

    /// <summary>
    /// 统一处理服务端启动与停止流程，并在启动后完成首轮真实进程快照采集。
    /// </summary>
    public async Task ToggleServerAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Info("服务端收到启动请求。");
            var started = await StartServerAsync();
            if (!started)
            {
                return;
            }

            StartBackgroundTimers();
            IsRunning = true;
            RaiseServerStateProperties();
            PublishServerStatusChanged();
            StartInitialSnapshotWarmup();

            await Log("服务端已启动，首轮进程快照将在后台静默预热，随后持续向客户端广播更新。");
            Logger.Info(
                $"当前生效配置：配置文件={_settingsFilePath}，TCP={_runtimeSettings.TcpIp}:{_runtimeSettings.TcpPort}，UDP={_runtimeSettings.UdpIp}:{_runtimeSettings.UdpPort}。");
            return;
        }

        StopBackgroundTimers();
        CancelProcessStructureChangedBroadcast();
        await TcpHelper.StopAsync();
        UdpHelper.Stop();
        IsRunning = false;
        RaiseServerStateProperties();
        PublishServerStatusChanged();
        Logger.Info("服务端收到停止请求。");
        await Log("服务端已停止。");
    }

    [EventHandler]
    private void ReceiveSocketClientChanged(SocketClientChangedCommand command)
    {
        if (!ReferenceEquals(command.Server, TcpHelper))
        {
            return;
        }

        Invoke(() =>
        {
            ConnectedClients.Clear();
            foreach (var item in TcpHelper.Clients
                         .Where(pair => pair.Value.TcpSocket != null)
                         .Select(pair => new KeyValuePair<string, Socket>(pair.Key, pair.Value.TcpSocket!)))
            {
                ConnectedClients.Add(item);
            }
        });
    }

    /// <summary>
    /// 统一分发客户端发来的 TCP 控制对象，并把每个请求纳入可追踪的日志链路。
    /// </summary>
    [EventHandler]
    private async Task ReceiveSocketMessageAsync(SocketCommand request)
    {
        if (await TryHandleSocketCommandAsync<RequestTargetType>(request, ReceiveSocketCommandAsync))
        {
            return;
        }

        if (await TryHandleSocketCommandAsync<RequestUdpAddress>(request, ReceiveSocketCommandAsync))
        {
            return;
        }

        if (await TryHandleSocketCommandAsync<RequestServiceInfo>(request, ReceiveSocketCommandAsync))
        {
            return;
        }

        if (await TryHandleSocketCommandAsync<RequestProcessIDList>(request, ReceiveSocketCommandAsync))
        {
            return;
        }

        if (await TryHandleSocketCommandAsync<RequestProcessList>(request, ReceiveSocketCommandAsync))
        {
            return;
        }

        if (await TryHandleSocketCommandAsync<RequestTerminateProcess>(request, ReceiveSocketCommandAsync))
        {
            return;
        }

        if (await TryHandleSocketCommandAsync<ChangeProcessList>(request, ReceiveSocketCommandAsync))
        {
            return;
        }

        await TryHandleSocketCommandAsync<Heartbeat>(request, ReceiveSocketCommandAsync);
    }

    private async Task<bool> StartServerAsync()
    {
        LoadRuntimeSettingsForStart();

        if (await TryStartServerCoreAsync())
        {
            return true;
        }

        RegeneratePorts();
        Logger.Warn(
            $"检测到端口冲突，已重新生成端口。TCP={_runtimeSettings.TcpPort}，UDP={_runtimeSettings.UdpPort}。");
        await Log("检测到端口被占用，已自动重新生成端口并重试一次。", LogType.Warn, false);

        if (await TryStartServerCoreAsync())
        {
            return true;
        }

        await Log("服务端启动失败，请检查网络环境或配置文件。", LogType.Error);
        return false;
    }

    private async Task<bool> TryStartServerCoreAsync()
    {
        var tcpResult = await TcpHelper.StartAsync("TCP服务端", _runtimeSettings.TcpIp, _runtimeSettings.TcpPort);
        if (!tcpResult.IsSuccess)
        {
            Logger.Error($"TCP 服务启动失败：{tcpResult.ErrorMessage}");
            return false;
        }

        var udpResult = UdpHelper.Start("UDP服务端", TcpHelper.SystemId, _runtimeSettings.UdpIp, _runtimeSettings.UdpPort);
        if (udpResult.IsSuccess)
        {
            return true;
        }

        Logger.Error($"UDP 组播启动失败：{udpResult.ErrorMessage}");
        await TcpHelper.StopAsync();
        return false;
    }

    private void LoadRuntimeSettingsForStart()
    {
        AppConfigHelper.TryGet(TcpIpKey, out string? tcpIp);
        AppConfigHelper.TryGet(TcpPortKey, out int tcpPort);
        AppConfigHelper.TryGet(UdpIpKey, out string? udpIp);
        AppConfigHelper.TryGet(UdpPortKey, out int udpPort);

        _runtimeSettings.TcpIp = string.IsNullOrWhiteSpace(tcpIp) ? IPAddress.Any.ToString() : tcpIp.Trim();
        if (!IPAddress.TryParse(_runtimeSettings.TcpIp, out _))
        {
            Logger.Warn($"TCP 配置地址无效，已回退为 {IPAddress.Any}：{_runtimeSettings.TcpIp}");
            _runtimeSettings.TcpIp = IPAddress.Any.ToString();
        }

        _runtimeSettings.TcpPort = tcpPort > 0 ? tcpPort : GetRandomAvailablePort(SocketType.Stream, ProtocolType.Tcp);
        _runtimeSettings.UdpIp = string.IsNullOrWhiteSpace(udpIp) ? GenerateRandomMulticastAddress() : udpIp.Trim();
        if (!IPAddress.TryParse(_runtimeSettings.UdpIp, out _))
        {
            var fallbackUdpIp = GenerateRandomMulticastAddress();
            Logger.Warn($"UDP 配置地址无效，已回退为 {fallbackUdpIp}：{_runtimeSettings.UdpIp}");
            _runtimeSettings.UdpIp = fallbackUdpIp;
        }

        _runtimeSettings.UdpPort = udpPort > 0 ? udpPort : GetRandomAvailablePort(SocketType.Dgram, ProtocolType.Udp);

        AppConfigHelper.SetOrAdd(TcpIpKey, _runtimeSettings.TcpIp);
        AppConfigHelper.SetOrAdd(TcpPortKey, _runtimeSettings.TcpPort);
        AppConfigHelper.SetOrAdd(UdpIpKey, _runtimeSettings.UdpIp);
        AppConfigHelper.SetOrAdd(UdpPortKey, _runtimeSettings.UdpPort);
    }

    private void RegeneratePorts()
    {
        _runtimeSettings.TcpPort = GetRandomAvailablePort(SocketType.Stream, ProtocolType.Tcp);
        _runtimeSettings.UdpPort = GetRandomAvailablePort(SocketType.Dgram, ProtocolType.Udp);
        AppConfigHelper.SetOrAdd(TcpPortKey, _runtimeSettings.TcpPort);
        AppConfigHelper.SetOrAdd(UdpPortKey, _runtimeSettings.UdpPort);
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestTargetType request)
    {
        await SendResponseAsync(client, new ResponseTargetType
        {
            TaskId = request.TaskId,
            Type = (byte)TerminalType.Server
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestUdpAddress request)
    {
        await SendResponseAsync(client, new ResponseUdpAddress
        {
            TaskId = request.TaskId,
            Ip = _runtimeSettings.UdpIp,
            Port = _runtimeSettings.UdpPort
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestServiceInfo request)
    {
        var readStartedAt = Stopwatch.GetTimestamp();
        var response = _processSnapshotProvider.GetServiceInfo(request.TaskId);
        var readElapsedMilliseconds = Stopwatch.GetElapsedTime(readStartedAt).TotalMilliseconds;

        var sendStartedAt = Stopwatch.GetTimestamp();
        await SendResponseAsync(client, response);
        var sendElapsedMilliseconds = Stopwatch.GetElapsedTime(sendStartedAt).TotalMilliseconds;

        Logger.Info(
            $"RequestServiceInfo 已完成：缓存读取 {readElapsedMilliseconds:F1} ms，响应发送 {sendElapsedMilliseconds:F1} ms。");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessIDList request)
    {
        await SendResponseAsync(client, new ResponseProcessIDList
        {
            TaskId = request.TaskId,
            IDList = _processSnapshotProvider.GetProcessIds()
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessList request)
    {
        await SendProcessPagesAsync(client, request.TaskId);
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestTerminateProcess request)
    {
        var success = _processSnapshotProvider.TryTerminateProcess(
            request.ProcessId,
            request.KillEntireProcessTree,
            out var message);

        await SendResponseAsync(client, new ResponseTerminateProcess
        {
            TaskId = request.TaskId,
            ProcessId = request.ProcessId,
            Success = success,
            Message = message
        });

        if (!success)
        {
            Logger.Warn($"结束进程失败，PID={request.ProcessId}，原因：{message}");
            return;
        }

        RefreshProcessSnapshot();
        ScheduleProcessStructureChangedBroadcast();
        Logger.Info($"结束进程成功，PID={request.ProcessId}。");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, ChangeProcessList changeProcess)
    {
        RefreshProcessSnapshot();
        ScheduleProcessStructureChangedBroadcast();
    }

    private async Task ReceiveSocketCommandAsync(Socket client, Heartbeat heartbeat)
    {
        await SendResponseAsync(client, new Heartbeat());
    }

    private void StartBackgroundTimers()
    {
        StopBackgroundTimers();

        _snapshotRefreshTimer = CreateTimer(SnapshotRefreshMilliseconds, SnapshotRefreshTimerOnElapsed);
        _sendRealtimeDataTimer = CreateTimer(RealtimeUdpMilliseconds, SendRealtimeDataTimerOnElapsed);
        _sendGeneralDataTimer = CreateTimer(GeneralUdpMilliseconds, SendGeneralDataTimerOnElapsed);
    }

    private void StopBackgroundTimers()
    {
        StopTimer(ref _snapshotRefreshTimer);
        StopTimer(ref _sendRealtimeDataTimer);
        StopTimer(ref _sendGeneralDataTimer);
    }

    private static Timer CreateTimer(double interval, System.Timers.ElapsedEventHandler handler)
    {
        var timer = new Timer(interval);
        timer.Elapsed += handler;
        timer.Start();
        return timer;
    }

    private static void StopTimer(ref Timer? timer)
    {
        if (timer == null)
        {
            return;
        }

        timer.Stop();
        timer.Dispose();
        timer = null;
    }

    private ProcessSnapshotRefreshResult RefreshProcessSnapshot()
    {
        var result = _processSnapshotProvider.RefreshSnapshot();
        CurrentProcessCount = result.ProcessCount;
        PublishServerStatusChanged();
        return result;
    }

    private void StartInitialSnapshotWarmup()
    {
        if (_initialSnapshotWarmupTask is { IsCompleted: false })
        {
            return;
        }

        _initialSnapshotWarmupTask = WarmupInitialSnapshotAsync();
    }

    /// <summary>
    /// 在后台线程刷新一次完整进程快照，避免首次启动采集阻塞 UI。
    /// </summary>
    private async Task<ProcessSnapshotRefreshResult> RefreshProcessSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(_processSnapshotProvider.RefreshSnapshot, cancellationToken);
        CurrentProcessCount = result.ProcessCount;
        PublishServerStatusChanged();
        return result;
    }

    private async Task WarmupInitialSnapshotAsync()
    {
        try
        {
            var result = await RefreshProcessSnapshotAsync();
            if (!TcpHelper.IsRunning)
            {
                return;
            }

            if (result.StructureChanged)
            {
                Logger.Info(
                    $"启动后台预热完成：当前数量={result.ProcessCount}，新增={result.AddedProcessCount}，退出={result.RemovedProcessCount}。");
                ScheduleProcessStructureChangedBroadcast();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("服务启动后的后台进程快照预热失败。", ex);
        }
    }

    private async void SnapshotRefreshTimerOnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!TcpHelper.IsRunning)
        {
            return;
        }

        try
        {
            var result = RefreshProcessSnapshot();
            if (result.StructureChanged)
            {
                Logger.Info(
                    $"进程结构发生变化：当前数量={result.ProcessCount}，新增={result.AddedProcessCount}，退出={result.RemovedProcessCount}。");
                ScheduleProcessStructureChangedBroadcast();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("刷新真实进程快照失败。", ex);
        }
    }

    private async void SendRealtimeDataTimerOnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        await PushUdpSnapshotAsync(
            _processSnapshotProvider.CalculateRealtimeUdpPage,
            _processSnapshotProvider.BuildRealtimeUpdatePage,
            "推送实时进程 UDP 数据失败。");
    }

    private async void SendGeneralDataTimerOnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        await PushUdpSnapshotAsync(
            _processSnapshotProvider.CalculateGeneralUdpPage,
            _processSnapshotProvider.BuildGeneralUpdatePage,
            "推送通用进程 UDP 数据失败。");
    }

    private async Task BroadcastProcessStructureChangedAsync()
    {
        var command = new ChangeProcessList();
        Logger.Info($"服务端 -> 客户端 TCP：{command}");
        await TcpHelper.SendCommandAsync(command);
    }

    private void ScheduleProcessStructureChangedBroadcast()
    {
        CancellationTokenSource currentCts;
        CancellationTokenSource? previousCts;
        lock (_processStructureChangeDebounceSyncRoot)
        {
            previousCts = _processStructureChangeDebounceCts;
            currentCts = new CancellationTokenSource();
            _processStructureChangeDebounceCts = currentCts;
        }

        previousCts?.Cancel();
        previousCts?.Dispose();
        _ = DebounceProcessStructureChangedBroadcastAsync(currentCts);
    }

    private async Task DebounceProcessStructureChangedBroadcastAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(ProcessStructureChangeDebounceMilliseconds, cts.Token);
            if (!TcpHelper.IsRunning)
            {
                return;
            }

            Logger.Info($"进程结构变化通知已静默 {ProcessStructureChangeDebounceMilliseconds} ms，开始广播。");
            await BroadcastProcessStructureChangedAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_processStructureChangeDebounceSyncRoot)
            {
                if (ReferenceEquals(_processStructureChangeDebounceCts, cts))
                {
                    _processStructureChangeDebounceCts = null;
                }
            }

            cts.Dispose();
        }
    }

    private void CancelProcessStructureChangedBroadcast()
    {
        CancellationTokenSource? cts;
        lock (_processStructureChangeDebounceSyncRoot)
        {
            cts = _processStructureChangeDebounceCts;
            _processStructureChangeDebounceCts = null;
        }

        cts?.Cancel();
        cts?.Dispose();
    }

    private void RaiseServerStateProperties()
    {
        this.RaisePropertyChanged(nameof(StartButtonText));
        this.RaisePropertyChanged(nameof(ServiceStatusText));
    }

    private void ConnectedClientsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(ClientCount));
        PublishServerStatusChanged();
    }

    private void Invoke(Action action)
    {
        Dispatcher.UIThread.Post(action.Invoke);
    }

    private async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action.Invoke);
    }

    private async Task Log(string message, LogType type = LogType.Info, bool showNotification = true)
    {
        switch (type)
        {
            case LogType.Warn:
                Logger.Warn(message);
                break;
            case LogType.Error:
                Logger.Error(message);
                break;
            default:
                Logger.Info(message);
                break;
        }

        if (!showNotification)
        {
            return;
        }

        var notificationType = type switch
        {
            LogType.Warn => NotificationType.Warning,
            LogType.Error => NotificationType.Error,
            _ => NotificationType.Information
        };

        await InvokeAsync(() => NotificationManager?.Show(new Notification("提示", message, notificationType)));
    }

    private static string GenerateRandomMulticastAddress() =>
        $"239.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";

    private static int GetRandomAvailablePort(SocketType socketType, ProtocolType protocolType)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, socketType, protocolType);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
    private async Task<bool> TryHandleSocketCommandAsync<TCommand>(
        SocketCommand request,
        Func<Socket, TCommand, Task> handler)
        where TCommand : new()
    {
        if (!request.IsCommand<TCommand>())
        {
            return false;
        }

        var command = request.GetCommand<TCommand>();
        Logger.Info($"客户端 -> 服务端 TCP：{command}");
        var startedAt = Stopwatch.GetTimestamp();
        await handler(request.Client!, command);
        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        Logger.Info($"服务端已完成 {typeof(TCommand).Name}，总耗时 {elapsedMilliseconds:F1} ms。");
        return true;
    }

    private Task SendResponseAsync(Socket client, CodeWF.NetWeaver.Base.INetObject response)
    {
        Logger.Info($"服务端 -> 客户端 TCP：{response}");
        return TcpHelper.SendCommandAsync(client, response);
    }

    /// <summary>
    /// 将完整进程树按页返回给客户端，避免单次响应包过大。
    /// </summary>
    private async Task SendProcessPagesAsync(Socket client, int taskId)
    {
        var processes = _processSnapshotProvider.GetAllProcesses();
        var totalSize = processes.Count;
        var pageCount = ProcessSnapshotProvider.GetPageCount(totalSize, ProcessPageSize);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            await SendResponseAsync(client, new ResponseProcessList
            {
                TaskId = taskId,
                TotalSize = totalSize,
                PageSize = ProcessPageSize,
                PageCount = pageCount,
                PageIndex = pageIndex,
                Processes = processes
                    .Skip(pageIndex * ProcessPageSize)
                    .Take(ProcessPageSize)
                    .ToList()
            });
        }
    }

    /// <summary>
    /// 周期性将快照压缩为 UDP 增量页发送给客户端，用于高频更新实时指标。
    /// </summary>
    private async Task PushUdpSnapshotAsync(
        UdpPageCalculator calculatePage,
        Func<int, int, CodeWF.NetWeaver.Base.INetObject> buildPage,
        string errorMessage)
    {
        if (!UdpHelper.IsRunning || !_processSnapshotProvider.IsInitialized)
        {
            return;
        }

        try
        {
            calculatePage(SerializeHelper.MaxUdpPacketSize, out var pageSize, out var pageCount);

            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                if (!UdpHelper.IsRunning)
                {
                    break;
                }

                var command = buildPage(pageSize, pageIndex);
                if (pageIndex == 0)
                {
                    //Logger.Info($"服务端 -> 客户端 UDP：{command}");
                }

                await UdpHelper.SendCommandAsync(command, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(errorMessage, ex);
        }
    }

    private delegate void UdpPageCalculator(int maxPacketSize, out int pageSize, out int pageCount);

    private void PublishServerStatusChanged() =>
        _ = EventBus.Default.PublishAsync(new ServerShellStatusChangedMessage(
            ServiceStatusText,
            CurrentProcessCount,
            ClientCount));

}
