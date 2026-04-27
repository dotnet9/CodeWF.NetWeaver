using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using ReactiveUI;
using SocketDto.Enums;
using SocketDto.Requests;
using SocketDto.Response;
using SocketTest.Client.Shell.Messages;
using SocketTest.Client.Shell.Services;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ClientConnectionPanelViewModel : ReactiveObject
{
    private readonly ClientApplicationStateService _appState;
    private readonly TcpSocketClient _tcpHelper;
    private readonly UdpSocketClient _udpHelper;
    private readonly ConcurrentDictionary<int, PendingRequestInfo> _pendingRequests = new();
    private bool _hasReceivedServiceInfo;
    private bool _hasReceivedUdpAddress;
    private bool _hasPublishedBootstrapCompleted;
    private int _timestampStartYear = 2020;

    public ClientConnectionPanelViewModel(
        ClientApplicationStateService appState,
        TcpSocketClient tcpHelper,
        UdpSocketClient udpHelper)
    {
        _appState = appState;
        _tcpHelper = tcpHelper;
        _udpHelper = udpHelper;

        UpdateSharedConnectionState();
        EventBus.Default.Subscribe(this);
    }

    public string TcpIp
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateSharedConnectionState();
        }
    } = "127.0.0.1";

    public int TcpPort
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateSharedConnectionState();
        }
    } = 5000;

    public string UdpIp
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateSharedConnectionState();
        }
    } = string.Empty;

    public int UdpPort
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateSharedConnectionState();
        }
    }

    public bool IsRunning => _tcpHelper.IsRunning;

    public string ConnectButtonText => _tcpHelper.IsRunning ? "断开连接" : "连接服务端";

    public string ConnectionSummary => _tcpHelper.IsRunning
        ? $"已连接到 {TcpIp}:{TcpPort}"
        : "尚未连接到 TCP 服务端";

    public string UdpSummary => string.IsNullOrWhiteSpace(UdpIp) || UdpPort <= 0
        ? "未建立 UDP 通道"
        : $"{UdpIp}:{UdpPort}";

    public async Task HandleConnectTcpAsync()
    {
        if (_tcpHelper.IsRunning)
        {
            Logger.Info($"客户端请求断开 TCP 连接：{TcpIp}:{TcpPort}");
            _udpHelper.Stop();
            _tcpHelper.Stop();
            ResetBootstrapState();
            RaiseConnectionProperties();
            await EventBus.Default.PublishAsync(new ClientConnectionStateChangedMessage(false));
            return;
        }

        Logger.Info($"客户端请求建立 TCP 连接：{TcpIp}:{TcpPort}");
        var result = await _tcpHelper.ConnectAsync("TCP客户端", TcpIp, TcpPort);
        if (!result.IsSuccess)
        {
            Logger.Warn(result.ErrorMessage ?? "TCP 连接失败。");
            RaiseConnectionProperties();
            return;
        }

        Logger.Info($"客户端已建立 TCP 连接：{TcpIp}:{TcpPort}");
        ResetBootstrapState();
        RaiseConnectionProperties();
        Logger.Info("客户端准备发布连接成功事件。");
        await EventBus.Default.PublishAsync(new ClientConnectionStateChangedMessage(true));
        Logger.Info("客户端连接成功事件已发布，开始请求连接初始化数据。");
        await RequestInitialDataAsync();
    }

    public async Task RequestInitialDataAsync()
    {
        if (!_tcpHelper.IsRunning)
        {
            return;
        }

        ResetBootstrapState();
        await SendTcpCommandAsync(new RequestTargetType { TaskId = NetHelper.GetTaskId() });
    }

    [EventHandler]
    public void ReceivedSocketCommand(SocketCommand message)
    {
        if (message.IsCommand<ResponseTargetType>())
        {
            var response = message.GetCommand<ResponseTargetType>();
            LogIncomingTcpCommand(response);
            ReceivedSocketMessage(response);
        }
        else if (message.IsCommand<ResponseServiceInfo>())
        {
            var response = message.GetCommand<ResponseServiceInfo>();
            LogIncomingTcpCommand(response);
            ReceivedSocketMessage(response);
        }
        else if (message.IsCommand<ResponseUdpAddress>())
        {
            var response = message.GetCommand<ResponseUdpAddress>();
            LogIncomingTcpCommand(response);
            ReceivedSocketMessage(response);
        }
    }

    private void ReceivedSocketMessage(ResponseTargetType response)
    {
        TryLogResponseElapsed(response.TaskId, nameof(ResponseTargetType));
        if (response.Type != (byte)TerminalType.Server)
        {
            Logger.Warn($"连接目标类型异常：{response.Type}");
            return;
        }

        Logger.Info("已确认连接目标为服务端。");
        _ = RequestBootstrapDetailsSafelyAsync();
    }

    private void ReceivedSocketMessage(ResponseServiceInfo response)
    {
        TryLogResponseElapsed(response.TaskId, nameof(ResponseServiceInfo));
        _timestampStartYear = response.TimestampStartYear;
        _hasReceivedServiceInfo = true;
        _appState.TimestampStartYear = response.TimestampStartYear;
        Logger.Info($"服务端信息：{response.OS}，时间基准年份：{response.TimestampStartYear}");
        if (!_hasReceivedUdpAddress)
        {
            _ = RequestUdpAddressSafelyAsync();
        }

        _ = PublishBootstrapCompletedIfReadyAsync();
    }

    private void ReceivedSocketMessage(ResponseUdpAddress response)
    {
        TryLogResponseElapsed(response.TaskId, nameof(ResponseUdpAddress));
        UdpIp = response.Ip ?? string.Empty;
        UdpPort = response.Port;
        _hasReceivedUdpAddress = true;
        Logger.Info($"已收到 UDP 组播地址：{UdpIp}:{UdpPort}");
        _ = ConnectUdpSafelyAsync();
        _ = PublishBootstrapCompletedIfReadyAsync();
    }

    private async Task RequestBootstrapDetailsAsync()
    {
        await SendTcpCommandAsync(new RequestServiceInfo { TaskId = NetHelper.GetTaskId() });
    }

    private async Task RequestBootstrapDetailsSafelyAsync()
    {
        try
        {
            await RequestBootstrapDetailsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("请求连接初始化基础信息失败。", ex);
        }
    }

    private async Task RequestUdpAddressSafelyAsync()
    {
        try
        {
            await SendTcpCommandAsync(new RequestUdpAddress { TaskId = NetHelper.GetTaskId() });
        }
        catch (Exception ex)
        {
            Logger.Error("请求 UDP 组播地址失败。", ex);
        }
    }

    private async Task ConnectUdpSafelyAsync()
    {
        if (_udpHelper.IsRunning || string.IsNullOrWhiteSpace(UdpIp) || UdpPort <= 0)
        {
            return;
        }

        if (!IPAddress.TryParse(UdpIp, out _))
        {
            Logger.Error($"收到非法 UDP 地址：{UdpIp}");
            return;
        }

        try
        {
            await _udpHelper.ConnectAsync("UDP客户端", UdpIp, UdpPort, _tcpHelper.LocalEndPoint!, _tcpHelper.SystemId);
        }
        catch (Exception ex)
        {
            Logger.Error("建立 UDP 通道失败。", ex);
        }
    }

    private async Task PublishBootstrapCompletedIfReadyAsync()
    {
        if (_hasPublishedBootstrapCompleted || !_hasReceivedServiceInfo || !_hasReceivedUdpAddress)
        {
            return;
        }

        _hasPublishedBootstrapCompleted = true;
        Logger.Info("连接初始化基础信息已齐备，准备通知进程模块加载进程数据。");
        await EventBus.Default.PublishAsync(new ClientConnectionBootstrapCompletedMessage(_timestampStartYear));
    }

    private async Task SendTcpCommandAsync(CodeWF.NetWeaver.Base.INetObject command)
    {
        TrackPendingRequest(command);
        Logger.Info($"客户端 -> 服务端 TCP：{command}");
        await _tcpHelper.SendCommandAsync(command);
    }

    private static void LogIncomingTcpCommand(object command) =>
        Logger.Info($"服务端 -> 客户端 TCP：{command}");

    private void ResetBootstrapState()
    {
        _pendingRequests.Clear();
        _hasReceivedServiceInfo = false;
        _hasReceivedUdpAddress = false;
        _hasPublishedBootstrapCompleted = false;
        _timestampStartYear = 2020;
        _appState.TimestampStartYear = _timestampStartYear;
        UdpIp = string.Empty;
        UdpPort = 0;
    }

    private void RaiseConnectionProperties()
    {
        this.RaisePropertyChanged(nameof(IsRunning));
        this.RaisePropertyChanged(nameof(ConnectionSummary));
        this.RaisePropertyChanged(nameof(ConnectButtonText));
        this.RaisePropertyChanged(nameof(UdpSummary));
        UpdateSharedConnectionState();
    }

    private void UpdateSharedConnectionState()
    {
        _appState.ConnectionSummary = ConnectionSummary;
        _appState.UdpSummary = UdpSummary;
    }

    private void TrackPendingRequest(CodeWF.NetWeaver.Base.INetObject command)
    {
        switch (command)
        {
            case RequestTargetType request:
                _pendingRequests[request.TaskId] = new PendingRequestInfo(nameof(RequestTargetType), Stopwatch.GetTimestamp());
                break;
            case RequestServiceInfo request:
                _pendingRequests[request.TaskId] = new PendingRequestInfo(nameof(RequestServiceInfo), Stopwatch.GetTimestamp());
                break;
            case RequestUdpAddress request:
                _pendingRequests[request.TaskId] = new PendingRequestInfo(nameof(RequestUdpAddress), Stopwatch.GetTimestamp());
                break;
        }
    }

    private void TryLogResponseElapsed(int taskId, string responseName)
    {
        if (!_pendingRequests.TryRemove(taskId, out var pending))
        {
            return;
        }

        var elapsedMilliseconds = Stopwatch.GetElapsedTime(pending.StartedAt).TotalMilliseconds;
        Logger.Info($"客户端收到 {responseName}，对应 {pending.RequestName} 往返耗时 {elapsedMilliseconds:F1} ms。");
    }

    private readonly record struct PendingRequestInfo(string RequestName, long StartedAt);
}
