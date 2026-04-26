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
using SocketTest.Server.Configuration;
using SocketTest.Server.Features.Processes.Services;
using SocketTest.Server.Shell.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

    private const string TcpIpKey = "TcpIp";
    private const string TcpPortKey = "TcpPort";
    private const string UdpIpKey = "UdpIp";
    private const string UdpPortKey = "UdpPort";

    private readonly IProcessSnapshotProvider _processSnapshotProvider;
    private readonly string _settingsFilePath;
    private readonly ServerRuntimeSettings _runtimeSettings = new();
    private Timer? _snapshotRefreshTimer;
    private Timer? _sendRealtimeDataTimer;
    private Timer? _sendGeneralDataTimer;

    public MainWindowViewModel()
        : this(ProcessSnapshotProviderFactory.CreateDefault())
    {
    }

    internal MainWindowViewModel(IProcessSnapshotProvider processSnapshotProvider)
    {
        _processSnapshotProvider = processSnapshotProvider;
        _settingsFilePath = AppContext.GetData("APP_CONFIG_FILE") as string
            ?? "应用配置文件";

        TcpHelper = new TcpSocketServer();
        UdpHelper = new UdpSocketServer();
        ConnectedClients.CollectionChanged += ConnectedClientsOnCollectionChanged;
        StatusBarViewModel = new ServerStatusBarViewModel();

        EventBus.Default.Subscribe(this);

        Logger.Info($"服务端配置文件：{_settingsFilePath}");
        Logger.Info("首次启动时会自动生成 TCP/UDP 配置并写入配置文件。");
        PublishServerStatusChanged();
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public TcpSocketServer TcpHelper { get; }

    public UdpSocketServer UdpHelper { get; }

    public ServerStatusBarViewModel StatusBarViewModel { get; }

    public ObservableCollection<KeyValuePair<string, Socket>> ConnectedClients { get; } = [];

    public bool IsRunning { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public int CurrentProcessCount { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public int ClientCount => ConnectedClients.Count;

    public string StartButtonText => IsRunning ? "停止服务" : "启动服务";

    public string ServiceStatusText => IsRunning ? "服务运行中" : "服务未启动";

    public async Task ToggleServerAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            var started = await StartServerAsync();
            if (!started)
            {
                return;
            }

            RefreshProcessSnapshot();
            StartBackgroundTimers();
            IsRunning = true;
            RaiseServerStateProperties();
            PublishServerStatusChanged();

            await Log("服务端已启动，正在实时采集真实进程并向客户端广播。");
            Logger.Info(
                $"当前生效配置：配置文件={_settingsFilePath}，TCP={_runtimeSettings.TcpIp}:{_runtimeSettings.TcpPort}，UDP={_runtimeSettings.UdpIp}:{_runtimeSettings.UdpPort}。");
            return;
        }

        StopBackgroundTimers();
        await TcpHelper.StopAsync();
        UdpHelper.Stop();
        IsRunning = false;
        RaiseServerStateProperties();
        PublishServerStatusChanged();
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
        _runtimeSettings.TcpPort = tcpPort > 0 ? tcpPort : GetRandomAvailablePort(SocketType.Stream, ProtocolType.Tcp);
        _runtimeSettings.UdpIp = string.IsNullOrWhiteSpace(udpIp) ? GenerateRandomMulticastAddress() : udpIp.Trim();
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
        EnsureProcessSnapshot();
        await SendResponseAsync(client, _processSnapshotProvider.GetServiceInfo(request.TaskId));
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessIDList request)
    {
        EnsureProcessSnapshot();
        await SendResponseAsync(client, new ResponseProcessIDList
        {
            TaskId = request.TaskId,
            IDList = _processSnapshotProvider.GetProcessIds()
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessList request)
    {
        EnsureProcessSnapshot();
        await SendProcessPagesAsync(client, request.TaskId);
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestTerminateProcess request)
    {
        var success = _processSnapshotProvider.TryTerminateProcess(
            request.ProcessId,
            request.KillEntireProcessTree,
            out var message);

        await TcpHelper.SendCommandAsync(client, new ResponseTerminateProcess
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
        await BroadcastProcessStructureChangedAsync();
        Logger.Info($"结束进程成功，PID={request.ProcessId}。");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, ChangeProcessList changeProcess)
    {
        RefreshProcessSnapshot();
        await BroadcastProcessStructureChangedAsync();
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

    private void EnsureProcessSnapshot()
    {
        if (!_processSnapshotProvider.IsInitialized)
        {
            RefreshProcessSnapshot();
        }
    }

    private bool RefreshProcessSnapshot()
    {
        var result = _processSnapshotProvider.RefreshSnapshot();
        CurrentProcessCount = result.ProcessCount;
        PublishServerStatusChanged();
        return result.StructureChanged;
    }

    private async void SnapshotRefreshTimerOnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!TcpHelper.IsRunning)
        {
            return;
        }

        try
        {
            if (RefreshProcessSnapshot())
            {
                await BroadcastProcessStructureChangedAsync();
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
        await TcpHelper.SendCommandAsync(new ChangeProcessList());
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

        await handler(request.Client!, request.GetCommand<TCommand>());
        return true;
    }

    private Task SendResponseAsync(Socket client, CodeWF.NetWeaver.Base.INetObject response) =>
        TcpHelper.SendCommandAsync(client, response);

    private async Task SendProcessPagesAsync(Socket client, int taskId)
    {
        var totalSize = _processSnapshotProvider.ProcessCount;
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
                Processes = _processSnapshotProvider.GetProcessPage(ProcessPageSize, pageIndex)
            });
        }
    }

    private async Task PushUdpSnapshotAsync(
        UdpPageCalculator calculatePage,
        Func<int, int, CodeWF.NetWeaver.Base.INetObject> buildPage,
        string errorMessage)
    {
        if (!UdpHelper.IsRunning)
        {
            return;
        }

        try
        {
            EnsureProcessSnapshot();
            calculatePage(SerializeHelper.MaxUdpPacketSize, out var pageSize, out var pageCount);

            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                if (!UdpHelper.IsRunning)
                {
                    break;
                }

                await UdpHelper.SendCommandAsync(buildPage(pageSize, pageIndex), DateTimeOffset.UtcNow);
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
