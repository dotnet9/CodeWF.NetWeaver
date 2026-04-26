using Avalonia.Threading;
using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using ReactiveUI;
using SocketDto;
using SocketDto.AutoCommand;
using SocketDto.Enums;
using SocketDto.Requests;
using SocketDto.Response;
using SocketDto.Udp;
using SocketTest.Client.Features.Processes.Models;
using SocketTest.Client.Infrastructure.Collections;
using SocketTest.Client.Shell.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SocketTest.Client.Features.Processes.ViewModels;

public class ProcessMonitorViewModel : ReactiveObject
{
    private readonly List<ProcessItemModel> _receivedProcesses = [];
    private readonly Dictionary<int, ProcessItemModel> _processLookup = [];
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ResponseTerminateProcess>> _pendingTerminateRequests = new();
    private int[]? _processIds;
    private int _timestampStartYear = 2020;
    private int _activeProcessTaskId = -1;
    private int _expectedProcessPages;
    private int _receivedProcessPages;
    private ProcessItemModel? _pendingTerminateProcess;

    public ProcessMonitorViewModel(TcpSocketClient tcpHelper, UdpSocketClient udpHelper)
    {
        TcpHelper = tcpHelper;
        UdpHelper = udpHelper;
        DisplayProcesses = new RangeObservableCollection<ProcessItemModel>();
        UdpHelper.Received += HandleUdpMessageReceived;

        EventBus.Default.Subscribe(this);
    }

    public RangeObservableCollection<ProcessItemModel> DisplayProcesses { get; }

    public string TcpIp { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "127.0.0.1";

    public int TcpPort { get; set => this.RaiseAndSetIfChanged(ref field, value); } = 5000;

    public string UdpIp
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(UdpSummary));
        }
    } = "239.255.255.250";

    public int UdpPort
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(UdpSummary));
        }
    } = 11012;

    public long SystemId { get; set => this.RaiseAndSetIfChanged(ref field, value); } = 1000;

    public bool IsRunning => TcpHelper.IsRunning;

    public string ConnectionSummary => TcpHelper.IsRunning
        ? $"已连接到 {TcpIp}:{TcpPort}"
        : "尚未连接到 TCP 服务端";

    public string ConnectButtonText => TcpHelper.IsRunning ? "断开连接" : "连接服务端";

    public string ProcessSummary => $"当前显示 {DisplayProcesses.Count:N0} 个进程";

    public string UdpSummary => $"{UdpIp}:{UdpPort}";

    public ProcessItemModel? SelectedProcess { get; set => this.RaiseAndSetIfChanged(ref field, value); }

    public bool IsTerminateDialogOpen { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public string TerminateDialogMessage { get; private set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;

    public TcpSocketClient TcpHelper { get; }

    public UdpSocketClient UdpHelper { get; }

    public async Task HandleConnectTcpAsync()
    {
        if (TcpHelper.IsRunning)
        {
            UdpHelper.Stop();
            TcpHelper.Stop();
            ClearProcesses();
            RaiseConnectionProperties();
            await EventBus.Default.PublishAsync(new ClientConnectionStateChangedMessage(false));
            return;
        }

        var result = await TcpHelper.ConnectAsync("SocketTest.Client", TcpIp, TcpPort);
        if (!result.IsSuccess)
        {
            Logger.Warn(result.ErrorMessage ?? "TCP 连接失败。");
            RaiseConnectionProperties();
            return;
        }

        RaiseConnectionProperties();
        await EventBus.Default.PublishAsync(new ClientConnectionStateChangedMessage(true));
        await RequestInitialDataAsync();
    }

    public async Task RequestInitialDataAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            return;
        }

        await TcpHelper.SendCommandAsync(new RequestTargetType { TaskId = NetHelper.GetTaskId() });
        await TcpHelper.SendCommandAsync(new RequestServiceInfo { TaskId = NetHelper.GetTaskId() });
        await TcpHelper.SendCommandAsync(new RequestUdpAddress { TaskId = NetHelper.GetTaskId() });
        await TcpHelper.SendCommandAsync(new RequestProcessIDList { TaskId = NetHelper.GetTaskId() });
        await RefreshProcessesAsync();
    }

    public async Task RefreshProcessesAsync()
    {
        if (TcpHelper.IsRunning)
        {
            await TcpHelper.SendCommandAsync(new RequestProcessList { TaskId = NetHelper.GetTaskId() });
        }
    }

    public void ShowTerminateProcessDialog(ProcessItemModel? process)
    {
        var targetProcess = process ?? SelectedProcess;
        if (targetProcess == null)
        {
            return;
        }

        _pendingTerminateProcess = targetProcess;
        TerminateDialogMessage =
            $"确认结束进程“{targetProcess.Name}”(PID: {targetProcess.PID})吗？\n服务端会同步结束该进程的所有子进程。";
        IsTerminateDialogOpen = true;
    }

    public async Task ConfirmTerminateProcessAsync()
    {
        var targetProcess = _pendingTerminateProcess;
        if (targetProcess == null)
        {
            IsTerminateDialogOpen = false;
            return;
        }

        IsTerminateDialogOpen = false;

        if (!TcpHelper.IsRunning)
        {
            Logger.Warn("尚未连接服务端，无法结束进程。");
            return;
        }

        var taskId = NetHelper.GetTaskId();
        var completionSource = new TaskCompletionSource<ResponseTerminateProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingTerminateRequests[taskId] = completionSource;

        await TcpHelper.SendCommandAsync(new RequestTerminateProcess
        {
            TaskId = taskId,
            ProcessId = targetProcess.PID,
            KillEntireProcessTree = true
        });

        try
        {
            var response = await completionSource.Task;
            if (!response.Success)
            {
                Logger.Warn(response.Message ?? $"结束进程失败，PID={targetProcess.PID}");
                return;
            }

            Logger.Info(response.Message ?? $"已结束进程：PID={targetProcess.PID}");
            await RequestInitialDataAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("结束进程请求失败。", ex);
        }
        finally
        {
            _pendingTerminateProcess = null;
        }
    }

    public void CancelTerminateProcessDialog()
    {
        _pendingTerminateProcess = null;
        IsTerminateDialogOpen = false;
    }

    public void ReceivedSocketCommand(SocketCommand message)
    {
        if (message.IsCommand<ResponseTargetType>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseTargetType>());
        }
        else if (message.IsCommand<ResponseUdpAddress>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseUdpAddress>());
        }
        else if (message.IsCommand<ResponseServiceInfo>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseServiceInfo>());
        }
        else if (message.IsCommand<ResponseProcessIDList>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseProcessIDList>());
        }
        else if (message.IsCommand<ResponseProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseProcessList>());
        }
        else if (message.IsCommand<ResponseTerminateProcess>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseTerminateProcess>());
        }
        else if (message.IsCommand<UpdateProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<UpdateProcessList>());
        }
        else if (message.IsCommand<ChangeProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<ChangeProcessList>());
        }
    }

    private void ReceivedSocketMessage(ResponseTargetType response)
    {
        if (response.Type == (byte)TerminalType.Server)
        {
            Logger.Info("已确认连接目标为服务端。");
        }
    }

    private void ReceivedSocketMessage(ResponseUdpAddress response)
    {
        UdpIp = response.Ip ?? UdpIp;
        UdpPort = response.Port;
        Logger.Info($"已收到 UDP 组播地址：{UdpIp}:{UdpPort}");

        if (!UdpHelper.IsRunning)
        {
            _ = UdpHelper.ConnectAsync("Server", UdpIp, UdpPort, string.Empty, SystemId);
        }
    }

    private void ReceivedSocketMessage(ResponseServiceInfo response)
    {
        _timestampStartYear = response.TimestampStartYear;
        Logger.Info($"服务端信息：{response.OS}，时间基准年份：{response.TimestampStartYear}");
    }

    private void ReceivedSocketMessage(ResponseProcessIDList response)
    {
        _processIds = response.IDList;
        RebuildProcessLookup();
    }

    private void ReceivedSocketMessage(ResponseProcessList response)
    {
        if (_activeProcessTaskId != response.TaskId || response.PageIndex == 0)
        {
            _activeProcessTaskId = response.TaskId;
            _expectedProcessPages = Math.Max(response.PageCount, 1);
            _receivedProcessPages = 0;
            _receivedProcesses.Clear();
        }

        foreach (var process in response.Processes ?? [])
        {
            _receivedProcesses.Add(new ProcessItemModel(process, _timestampStartYear));
        }

        _receivedProcessPages++;
        if (_receivedProcessPages < _expectedProcessPages)
        {
            return;
        }

        RebuildProcessLookup();
        RefreshProcessGrid();
        Logger.Info($"已接收完整进程列表，共 {_receivedProcesses.Count:N0} 项。");
    }

    private void ReceivedSocketMessage(ResponseTerminateProcess response)
    {
        if (_pendingTerminateRequests.TryRemove(response.TaskId, out var completionSource))
        {
            completionSource.TrySetResult(response);
        }
    }

    private void ReceivedSocketMessage(UpdateProcessList response)
    {
        if (_receivedProcesses.Count == 0 || response.Processes == null)
        {
            return;
        }

        for (var i = 0; i < response.Processes.Count && i < _receivedProcesses.Count; i++)
        {
            var updateInfo = response.Processes[i];
            var process = _receivedProcesses[i];
            process.Status = (ProcessStatus)updateInfo.ProcessStatus;
            process.AlarmStatus = (AlarmStatus)updateInfo.AlarmStatus;
            process.Cpu = updateInfo.Cpu;
            process.Memory = updateInfo.Memory;
            process.Disk = updateInfo.Disk;
            process.Network = updateInfo.Network;
            process.Gpu = updateInfo.Gpu;
            process.PowerUsage = (PowerUsage)updateInfo.PowerUsage;
            process.UpdateTime = DateTime.Now;
        }

        RefreshProcessGrid();
    }

    private void ReceivedSocketMessage(ChangeProcessList response)
    {
        Logger.Info("服务端通知进程结构已变化，准备重新拉取进程列表。");
        _ = RequestInitialDataAsync();
    }

    private void HandleUdpMessageReceived(object? sender, SocketCommand message)
    {
        try
        {
            if (message.IsCommand<UpdateRealtimeProcessList>())
            {
                ApplyRealtimeUdpUpdate(message.GetCommand<UpdateRealtimeProcessList>());
            }
            else if (message.IsCommand<UpdateGeneralProcessList>())
            {
                ApplyGeneralUdpUpdate(message.GetCommand<UpdateGeneralProcessList>());
            }
        }
        catch (Exception ex)
        {
            Logger.Error("处理 UDP 增量进程数据失败。", ex);
        }
    }

    private void ApplyRealtimeUdpUpdate(UpdateRealtimeProcessList response)
    {
        if (_receivedProcesses.Count == 0)
        {
            return;
        }

        var startIndex = response.PageIndex * response.PageSize;
        var count = Math.Min(response.Cpus.Length / sizeof(short), _receivedProcesses.Count - startIndex);
        for (var i = 0; i < count; i++)
        {
            var process = _receivedProcesses[startIndex + i];
            process.Update(
                BitConverter.ToInt16(response.Cpus, i * sizeof(short)),
                BitConverter.ToInt16(response.Memories, i * sizeof(short)),
                BitConverter.ToInt16(response.Disks, i * sizeof(short)),
                BitConverter.ToInt16(response.Networks, i * sizeof(short)));
        }

        RefreshProcessGrid();
    }

    private void ApplyGeneralUdpUpdate(UpdateGeneralProcessList response)
    {
        if (_receivedProcesses.Count == 0)
        {
            return;
        }

        var startIndex = response.PageIndex * response.PageSize;
        var count = Math.Min(response.ProcessStatuses.Length, _receivedProcesses.Count - startIndex);
        for (var i = 0; i < count; i++)
        {
            var process = _receivedProcesses[startIndex + i];
            process.Update(
                _timestampStartYear,
                response.ProcessStatuses[i],
                response.AlarmStatuses[i],
                BitConverter.ToInt16(response.Gpus, i * sizeof(short)),
                response.GpuEngine[i],
                response.PowerUsage[i],
                response.PowerUsageTrend[i],
                BitConverter.ToUInt32(response.UpdateTimes, i * sizeof(uint)));
        }

        RefreshProcessGrid();
    }

    private void RebuildProcessLookup()
    {
        _processLookup.Clear();
        foreach (var process in _receivedProcesses)
        {
            _processLookup[process.PID] = process;
        }

        if (_processIds == null || _processIds.Length == 0)
        {
            return;
        }

        var orderedProcesses = new List<ProcessItemModel>(_processIds.Length);
        foreach (var processId in _processIds)
        {
            if (_processLookup.TryGetValue(processId, out var process))
            {
                orderedProcesses.Add(process);
            }
        }

        if (orderedProcesses.Count > 0)
        {
            _receivedProcesses.Clear();
            _receivedProcesses.AddRange(orderedProcesses);
        }
    }

    private void RefreshProcessGrid()
    {
        Dispatcher.UIThread.Post(() =>
        {
            DisplayProcesses.Clear();
            DisplayProcesses.AddRange(_receivedProcesses);
            if (SelectedProcess != null)
            {
                SelectedProcess = DisplayProcesses.FirstOrDefault(item => item.PID == SelectedProcess.PID);
            }

            this.RaisePropertyChanged(nameof(ProcessSummary));
        });
    }

    private void ClearProcesses()
    {
        _receivedProcesses.Clear();
        _processLookup.Clear();
        _processIds = null;
        DisplayProcesses.Clear();
        SelectedProcess = null;
        this.RaisePropertyChanged(nameof(ProcessSummary));
    }

    private void RaiseConnectionProperties()
    {
        this.RaisePropertyChanged(nameof(IsRunning));
        this.RaisePropertyChanged(nameof(ConnectionSummary));
        this.RaisePropertyChanged(nameof(ConnectButtonText));
    }
}
