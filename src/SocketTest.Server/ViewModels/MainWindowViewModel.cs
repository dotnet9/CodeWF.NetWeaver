using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using CodeWF.NetWrapper.Response;
using CodeWF.Tools.Helpers;
using ReactiveUI;
using SocketDto;
using SocketDto.AutoCommand;
using SocketDto.Enums;
using SocketDto.Requests;
using SocketDto.Response;
using SocketDto.Udp;
using SocketTest.Server.Configuration;
using SocketTest.Server.Dtos;
using SocketTest.Server.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Notification = Avalonia.Controls.Notifications.Notification;
using Timer = System.Timers.Timer;

namespace SocketTest.Server.ViewModels;

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
        _settingsFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;

        TcpHelper = new TcpSocketServer();
        UdpHelper = new UdpSocketServer();

        ConnectedClients.CollectionChanged += ConnectedClientsOnCollectionChanged;
        EventBus.Default.Subscribe(this);

        Logger.Info($"服务端配置文件：{_settingsFilePath}");
        Logger.Info("当前未生成 TCP/UDP 配置，点击“启动服务”后会按需生成并写回配置文件。");
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public TcpSocketServer TcpHelper { get; }

    public UdpSocketServer UdpHelper { get; }

    public ObservableCollection<KeyValuePair<string, Socket>> ConnectedClients { get; } = new();

    public bool IsRunning
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int CurrentProcessCount
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public DateTime HeartbeatTime
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

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
        await Log("服务端已停止。");
    }

    private async Task<bool> StartServerAsync()
    {
        LoadRuntimeSettingsForStart();

        var firstTry = await TryStartServerCoreAsync();
        if (firstTry)
        {
            return true;
        }

        RegeneratePorts();
        Logger.Warn(
            $"检测到启动端口冲突，已自动重生端口并写回配置文件。新的 TCP 端口：{_runtimeSettings.TcpPort}，新的 UDP 端口：{_runtimeSettings.UdpPort}。");
        await Log("检测到端口被占用，已自动重生端口并重试一次。", LogType.Warn, false);

        var secondTry = await TryStartServerCoreAsync();
        if (secondTry)
        {
            return true;
        }

        await Log("服务端启动失败，请检查系统网络环境或配置文件。", LogType.Error);
        return false;
    }

    private async Task<bool> TryStartServerCoreAsync()
    {
        TcpHelper.FileSaveDirectory = null;

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
        if (request.IsCommand<RequestTargetType>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<RequestTargetType>());
        }
        else if (request.IsCommand<RequestUdpAddress>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<RequestUdpAddress>());
        }
        else if (request.IsCommand<RequestServiceInfo>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<RequestServiceInfo>());
        }
        else if (request.IsCommand<RequestProcessIDList>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<RequestProcessIDList>());
        }
        else if (request.IsCommand<RequestProcessList>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<RequestProcessList>());
        }
        else if (request.IsCommand<RequestTerminateProcess>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<RequestTerminateProcess>());
        }
        else if (request.IsCommand<ChangeProcessList>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<ChangeProcessList>());
        }
        else if (request.IsCommand<Heartbeat>())
        {
            await ReceiveSocketCommandAsync(request.Client!, request.GetCommand<Heartbeat>());
        }
        else if (request.IsCommand<RequestStudentListCorrect>())
        {
            await ReceiveRequestStudentListCorrectAsync(request.Client!, request);
        }
        else if (request.IsCommandDiffVersion<RequestStudentListDiffVersion>())
        {
            await ReceiveRequestStudentListDiffVersionAsync(request.Client!, request);
        }
        else
        {
            await ReceiveSocketCommandAsync(request.Client!, request);
        }
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestTargetType request)
    {
        await TcpHelper.SendCommandAsync(client, new ResponseTargetType
        {
            TaskId = request.TaskId,
            Type = (byte)TerminalType.Server
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestUdpAddress request)
    {
        await TcpHelper.SendCommandAsync(client, new ResponseUdpAddress
        {
            TaskId = request.TaskId,
            Ip = _runtimeSettings.UdpIp,
            Port = _runtimeSettings.UdpPort
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestServiceInfo request)
    {
        EnsureProcessSnapshot();
        await TcpHelper.SendCommandAsync(client, _processSnapshotProvider.GetServiceInfo(request.TaskId));
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessIDList request)
    {
        EnsureProcessSnapshot();
        await TcpHelper.SendCommandAsync(client, new ResponseProcessIDList
        {
            TaskId = request.TaskId,
            IDList = _processSnapshotProvider.GetProcessIds()
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessList request)
    {
        EnsureProcessSnapshot();

        var totalSize = _processSnapshotProvider.ProcessCount;
        var pageCount = ProcessSnapshotProvider.GetPageCount(totalSize, ProcessPageSize);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            await TcpHelper.SendCommandAsync(client, new ResponseProcessList
            {
                TaskId = request.TaskId,
                TotalSize = totalSize,
                PageSize = ProcessPageSize,
                PageCount = pageCount,
                PageIndex = pageIndex,
                Processes = _processSnapshotProvider.GetProcessPage(ProcessPageSize, pageIndex)
            });
        }
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
        await TcpHelper.SendCommandAsync(client, new Heartbeat());
        HeartbeatTime = DateTime.Now;
    }

    private async Task ReceiveRequestStudentListCorrectAsync(Socket client, SocketCommand request)
    {
        try
        {
            var command = request.GetCommand<RequestStudentListCorrect>();
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Success(command.TaskId));
        }
        catch (Exception)
        {
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, $"{request.HeadInfo}"));
        }
    }

    private async Task ReceiveRequestStudentListDiffVersionAsync(Socket client, SocketCommand request)
    {
        try
        {
            var currentNetHead = SerializeHelper.GetNetObjectHead<RequestStudentListDiffVersion>();
            var errorMessage =
                $"命令版本异常：命令 ID={request.HeadInfo.ObjectId}，服务端版本={currentNetHead.Version}，客户端版本={request.HeadInfo.ObjectVersion}";
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, errorMessage));
        }
        catch (Exception)
        {
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, $"{request.HeadInfo}"));
        }
    }

    private async Task ReceiveSocketCommandAsync(Socket client, SocketCommand request)
    {
        try
        {
            var currentNetHead = SerializeHelper.GetNetObjectHead<RequestStudentListDiffProps>();
            if (currentNetHead.Id == request.HeadInfo.ObjectId &&
                currentNetHead.Version == request.HeadInfo.ObjectVersion)
            {
                _ = request.GetCommand<RequestStudentListDiffProps>();
            }
            else
            {
                await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, $"{request.HeadInfo}"));
            }
        }
        catch (Exception ex)
        {
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, $"{request.HeadInfo}，{ex.Message}"));
        }
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
        if (_processSnapshotProvider.IsInitialized)
        {
            return;
        }

        RefreshProcessSnapshot();
    }

    private bool RefreshProcessSnapshot()
    {
        var result = _processSnapshotProvider.RefreshSnapshot();
        CurrentProcessCount = result.ProcessCount;
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
        if (!UdpHelper.IsRunning)
        {
            return;
        }

        try
        {
            EnsureProcessSnapshot();
            _processSnapshotProvider.CalculateRealtimeUdpPage(SerializeHelper.MaxUdpPacketSize, out var pageSize, out var pageCount);

            var sw = Stopwatch.StartNew();
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                if (!UdpHelper.IsRunning)
                {
                    break;
                }

                var response = _processSnapshotProvider.BuildRealtimeUpdatePage(pageSize, pageIndex);
                await UdpHelper.SendCommandAsync(response, DateTimeOffset.UtcNow);
            }

            Logger.Info($"已推送实时进程 UDP 数据，共 {CurrentProcessCount} 条，分页 {pageCount}，耗时 {sw.ElapsedMilliseconds}ms。");
        }
        catch (Exception ex)
        {
            Logger.Error("推送实时进程 UDP 数据失败。", ex);
        }
    }

    private async void SendGeneralDataTimerOnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!UdpHelper.IsRunning)
        {
            return;
        }

        try
        {
            EnsureProcessSnapshot();
            _processSnapshotProvider.CalculateGeneralUdpPage(SerializeHelper.MaxUdpPacketSize, out var pageSize, out var pageCount);

            var sw = Stopwatch.StartNew();
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                if (!UdpHelper.IsRunning)
                {
                    break;
                }

                var response = _processSnapshotProvider.BuildGeneralUpdatePage(pageSize, pageIndex);
                await UdpHelper.SendCommandAsync(response, DateTimeOffset.UtcNow);
            }

            Logger.Info($"已推送通用进程 UDP 数据，共 {CurrentProcessCount} 条，分页 {pageCount}，耗时 {sw.ElapsedMilliseconds}ms。");
        }
        catch (Exception ex)
        {
            Logger.Error("推送通用进程 UDP 数据失败。", ex);
        }
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
        if (type == LogType.Info)
        {
            Logger.Info(message);
        }
        else if (type == LogType.Warn)
        {
            Logger.Warn(message);
        }
        else if (type == LogType.Error)
        {
            Logger.Error(message);
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
}
