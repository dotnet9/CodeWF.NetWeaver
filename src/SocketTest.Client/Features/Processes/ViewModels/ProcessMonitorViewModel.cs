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
using SocketTest.Client.Shell.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SocketTest.Client.Features.Processes.ViewModels;

public class ProcessMonitorViewModel : ReactiveObject
{
    private const int ProcessStructureChangeDebounceMilliseconds = 1000;

    private readonly List<ProcessItemModel> _receivedProcesses = [];
    private readonly Dictionary<int, ProcessItemModel> _processLookup = [];
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ResponseTerminateProcess>> _pendingTerminateRequests = new();
    private readonly ConcurrentDictionary<int, PendingRequestInfo> _pendingRequests = new();
    private readonly ClientApplicationStateService _appState;
    private readonly object _processStructureChangeDebounceSyncRoot = new();
    private bool _isGridRefreshQueued;
    private bool _isProcessIdRequestInFlight;
    private bool _isProcessListRequestInFlight;
    private bool _pendingProcessRefresh;
    private int[]? _processIds;
    private int _timestampStartYear = 2020;
    private int _activeProcessTaskId = -1;
    private int _expectedProcessPages;
    private int _receivedProcessPages;
    private CancellationTokenSource? _processStructureChangeDebounceCts;
    private ProcessItemModel? _pendingTerminateProcess;

    public ProcessMonitorViewModel(
        TcpSocketClient tcpHelper,
        UdpSocketClient udpHelper,
        ClientApplicationStateService appState)
    {
        _appState = appState;
        TcpHelper = tcpHelper;
        UdpHelper = udpHelper;
        DisplayProcesses = new RangeObservableCollection<ProcessItemModel>();
        UdpHelper.Received += HandleUdpMessageReceived;

        EventBus.Default.Subscribe(this);
        UpdateProcessSummaryState();
    }

    public RangeObservableCollection<ProcessItemModel> DisplayProcesses { get; }

    public string ProcessSummary => $"当前显示 {DisplayProcesses.Count:N0} 个进程";

    public ProcessItemModel? SelectedProcess { get; set => this.RaiseAndSetIfChanged(ref field, value); }

    public bool IsTerminateDialogOpen { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public string TerminateDialogMessage { get; private set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;

    public TcpSocketClient TcpHelper { get; }

    public UdpSocketClient UdpHelper { get; }

    public Task RequestInitialDataAsync()
    {
        RequestProcessRefresh();
        return Task.CompletedTask;
    }

    public Task RefreshProcessesAsync()
    {
        RequestProcessRefresh();
        return Task.CompletedTask;
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
            $"确认结束进程 \"{targetProcess.Name}\" (PID: {targetProcess.PID}) 吗？\n服务端会同步结束该进程的所有子进程。";
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

        await SendTcpCommandAsync(new RequestTerminateProcess
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
            RequestProcessRefresh();
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

    [EventHandler]
    private void ReceiveClientConnectionStateChanged(ClientConnectionStateChangedMessage message)
    {
        if (!message.IsConnected)
        {
            ClearProcesses();
        }
    }

    [EventHandler]
    private void ReceiveClientConnectionBootstrapCompleted(ClientConnectionBootstrapCompletedMessage message)
    {
        _timestampStartYear = message.TimestampStartYear;
        RequestProcessRefresh();
    }

    [EventHandler]
    public void ReceivedSocketCommand(SocketCommand message)
    {
        if (message.IsCommand<ResponseProcessIDList>())
        {
            var response = message.GetCommand<ResponseProcessIDList>();
            LogIncomingTcpCommand(response);
            ReceivedSocketMessage(response);
        }
        else if (message.IsCommand<ResponseProcessList>())
        {
            var response = message.GetCommand<ResponseProcessList>();
            LogIncomingTcpCommand(response);
            ReceivedSocketMessage(response);
        }
        else if (message.IsCommand<ResponseTerminateProcess>())
        {
            var response = message.GetCommand<ResponseTerminateProcess>();
            LogIncomingTcpCommand(response);
            ReceivedSocketMessage(response);
        }
        else if (message.IsCommand<UpdateProcessList>())
        {
            var response = message.GetCommand<UpdateProcessList>();
            LogIncomingUdpCommand(response);
            ReceivedSocketMessage(response);
        }
        else if (message.IsCommand<ChangeProcessList>())
        {
            var response = message.GetCommand<ChangeProcessList>();
            LogIncomingTcpCommand(response);
            ReceivedSocketMessage(response);
        }
    }

    private void ReceivedSocketMessage(ResponseProcessIDList response)
    {
        TryLogResponseElapsed(response.TaskId, nameof(ResponseProcessIDList));
        _isProcessIdRequestInFlight = false;
        _processIds = response.IDList;
        RebuildProcessLookup();

        if (_processIds == null || _processIds.Length == 0)
        {
            _receivedProcesses.Clear();
            RefreshProcessGrid();
            StartPendingProcessRefreshIfNeeded();
            return;
        }

        _ = RequestProcessListSafelyAsync();
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

        TryLogResponseElapsed(response.TaskId, nameof(ResponseProcessList));
        _isProcessListRequestInFlight = false;
        RebuildProcessLookup();
        RefreshProcessGrid();
        StartPendingProcessRefreshIfNeeded();
        Logger.Info($"已接收完整进程列表，共 {_receivedProcesses.Count:N0} 条。");
    }

    private void ReceivedSocketMessage(ResponseTerminateProcess response)
    {
        TryLogResponseElapsed(response.TaskId, nameof(ResponseTerminateProcess));
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
        RequestProcessRefreshDebounced();
    }

    private void HandleUdpMessageReceived(object? sender, SocketCommand message)
    {
        try
        {
            if (message.IsCommand<UpdateRealtimeProcessList>())
            {
                var response = message.GetCommand<UpdateRealtimeProcessList>();
                LogIncomingUdpCommand(response);
                ApplyRealtimeUdpUpdate(response);
            }
            else if (message.IsCommand<UpdateGeneralProcessList>())
            {
                var response = message.GetCommand<UpdateGeneralProcessList>();
                LogIncomingUdpCommand(response);
                ApplyGeneralUdpUpdate(response);
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
        if (_isGridRefreshQueued)
        {
            return;
        }

        _isGridRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                DisplayProcesses.Clear();
                DisplayProcesses.AddRange(_receivedProcesses);
                if (SelectedProcess != null)
                {
                    SelectedProcess = DisplayProcesses.FirstOrDefault(item => item.PID == SelectedProcess.PID);
                }

                this.RaisePropertyChanged(nameof(ProcessSummary));
                UpdateProcessSummaryState();
            }
            finally
            {
                _isGridRefreshQueued = false;
            }
        });
    }

    private void ClearProcesses()
    {
        CancelProcessRefreshDebounce();
        _receivedProcesses.Clear();
        _processLookup.Clear();
        _pendingTerminateRequests.Clear();
        _pendingRequests.Clear();
        _processIds = null;
        _activeProcessTaskId = -1;
        _expectedProcessPages = 0;
        _receivedProcessPages = 0;
        _isProcessIdRequestInFlight = false;
        _isProcessListRequestInFlight = false;
        _pendingProcessRefresh = false;
        _pendingTerminateProcess = null;
        DisplayProcesses.Clear();
        SelectedProcess = null;
        this.RaisePropertyChanged(nameof(ProcessSummary));
        UpdateProcessSummaryState();
    }

    private async Task RequestProcessIdListAsync()
    {
        if (!TcpHelper.IsRunning || _isProcessIdRequestInFlight)
        {
            return;
        }

        _isProcessIdRequestInFlight = true;
        await SendTcpCommandAsync(new RequestProcessIDList { TaskId = NetHelper.GetTaskId() });
    }

    private async Task RequestProcessListAsync()
    {
        if (!TcpHelper.IsRunning || _isProcessListRequestInFlight)
        {
            return;
        }

        _isProcessListRequestInFlight = true;
        await SendTcpCommandAsync(new RequestProcessList { TaskId = NetHelper.GetTaskId() });
    }

    private async Task SendTcpCommandAsync(CodeWF.NetWeaver.Base.INetObject command)
    {
        TrackPendingRequest(command);
        Logger.Info($"客户端 -> 服务端 TCP：{command}");
        await TcpHelper.SendCommandAsync(command);
    }

    private async Task RequestProcessIdListSafelyAsync()
    {
        try
        {
            await RequestProcessIdListAsync();
        }
        catch (Exception ex)
        {
            _isProcessIdRequestInFlight = false;
            Logger.Error("请求进程 ID 列表失败。", ex);
        }
    }

    private async Task RequestProcessListSafelyAsync()
    {
        try
        {
            await RequestProcessListAsync();
        }
        catch (Exception ex)
        {
            _isProcessListRequestInFlight = false;
            Logger.Error("请求进程详细列表失败。", ex);
        }
    }

    private void RequestProcessRefresh()
    {
        _pendingProcessRefresh = true;
        StartPendingProcessRefreshIfNeeded();
    }

    private void RequestProcessRefreshDebounced()
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
        _ = DebounceProcessRefreshAsync(currentCts);
    }

    private async Task DebounceProcessRefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(ProcessStructureChangeDebounceMilliseconds, cts.Token);
            Logger.Info($"客户端已静默等待 {ProcessStructureChangeDebounceMilliseconds} ms，开始重新请求进程列表。");
            RequestProcessRefresh();
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

    private void StartPendingProcessRefreshIfNeeded()
    {
        if (!_pendingProcessRefresh || _isProcessIdRequestInFlight || _isProcessListRequestInFlight || !TcpHelper.IsRunning)
        {
            return;
        }

        _pendingProcessRefresh = false;
        _ = RequestProcessIdListSafelyAsync();
    }

    private void CancelProcessRefreshDebounce()
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

    private void TrackPendingRequest(CodeWF.NetWeaver.Base.INetObject command)
    {
        switch (command)
        {
            case RequestProcessIDList request:
                _pendingRequests[request.TaskId] = new PendingRequestInfo(nameof(RequestProcessIDList), Stopwatch.GetTimestamp());
                break;
            case RequestProcessList request:
                _pendingRequests[request.TaskId] = new PendingRequestInfo(nameof(RequestProcessList), Stopwatch.GetTimestamp());
                break;
            case RequestTerminateProcess request:
                _pendingRequests[request.TaskId] = new PendingRequestInfo(nameof(RequestTerminateProcess), Stopwatch.GetTimestamp());
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

    private static void LogIncomingTcpCommand(object command) =>
        Logger.Info($"服务端 -> 客户端 TCP：{command}");

    private static void LogIncomingUdpCommand(object command)
    {
        if (command is UpdateRealtimeProcessList realtime && realtime.PageIndex > 0)
        {
            return;
        }

        if (command is UpdateGeneralProcessList general && general.PageIndex > 0)
        {
            return;
        }

        // Logger.Info($"服务端 -> 客户端 UDP：{command}");
    }

    private void UpdateProcessSummaryState()
    {
        _appState.ProcessSummary = ProcessSummary;
    }
}
