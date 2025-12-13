using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using CodeWF.Tools.Extensions;
using ReactiveUI;
using SocketDto;
using SocketDto.AutoCommand;
using SocketDto.Enums;
using SocketDto.Requests;
using SocketDto.Response;
using SocketDto.Udp;
using SocketTest.Client.Extensions;
using SocketTest.Client.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using DynamicData;
using Notification = Avalonia.Controls.Notifications.Notification;
using Timer = System.Timers.Timer;

namespace SocketTest.Client.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public WindowNotificationManager? NotificationManager { get; set; }
    private TcpSocketClient TcpClient { get; } = new();
    private UdpSocketClient UdpClient { get; } = new();

    private readonly Subject<DateTime> _refreshSubject = new();
    private const int SyncPointsInterval = 1000;


    private int[]? _processIdArray;
    private ObservableAsPropertyHelper<bool> _isEmpty;
    private ImmutableList<ProcessItemModel> _tempProcesses = [];
    private readonly Dictionary<int, ProcessItemModel> _processIdAndItems = [];

    private readonly ConcurrentDictionary<int, UpdateGeneralProcessList> _receivedGeneralProcessLists = new();
    private readonly ConcurrentDictionary<int, UpdateRealtimeProcessList> _receivedRealtimeProcessLists = new();


    /// <summary>
    ///     搜索关键词
    /// </summary>
    public string? SearchKey
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private Timer? _sendDataTimer;
    private int _timestampStartYear;

    public MainWindowViewModel()
    {
        void RegisterCommand()
        {
            RefreshCommand = ReactiveCommand.CreateFromTask(HandleRefreshCommandAsync);
            RefreshAllCommand = ReactiveCommand.CreateFromTask(HandleRefreshAllCommandAsync);
            ConnectCommand = ReactiveCommand.CreateFromTask(HandleConnectTcpCommandAsync);
        }

        EventBus.Default.Subscribe(this);
        RegisterCommand();

        Logger.Info("连接服务端后获取数据");

        _itemSourceCache.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out _displayProcesses)
            .Subscribe();

        _isEmpty = this.WhenAnyValue(m => m.DisplayProcesses.Count)
            .Select(count => count == 0)
            .ToProperty(this, p => p.IsEmpty);

        _refreshSubject
            .Throttle(TimeSpan.FromMilliseconds(SyncPointsInterval))
            .Subscribe(_ => ApplyFilter(), onError: ex => Logger.Error("订阅刷新列表事件失败", ex));
    }

    #region 属性

    public bool IsEmpty => _isEmpty.Value;
    public Window? Owner { get; set; }
    private HashSet<int>? _previousProcessHashSets;
    private SourceCache<ProcessItemModel, int> _itemSourceCache = new(item => item.PID);

    private readonly ReadOnlyObservableCollection<ProcessItemModel> _displayProcesses;
    public ReadOnlyObservableCollection<ProcessItemModel> DisplayProcesses => _displayProcesses;

    public string? IP
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "127.0.0.1";

    public int Port
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 5000;

    public bool IsRunning
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     基本信息
    /// </summary>
    public string? BaseInfo
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }


    /// <summary>
    ///     刷新数据
    /// </summary>
    public ReactiveCommand<Unit, Unit>? RefreshCommand { get; private set; }

    /// <summary>
    ///     刷新数据
    /// </summary>
    public ReactiveCommand<Unit, Unit>? RefreshAllCommand { get; private set; }

    /// <summary>
    ///     连接命令
    /// </summary>
    public ReactiveCommand<Unit, Unit>? ConnectCommand { get; private set; }

    #endregion


    public async Task HandleConnectTcpCommandAsync()
    {
        try
        {
            if (IsRunning)
            {
                // 清理定时器资源
                StopHeartbeat();
                StopUpdateDisplayProcesses();
                StopUpdateProcesses(); // 停止UDP数据更新任务
                TcpClient.Stop();
                UdpClient.Stop();

                UdpClient.Received -= ReceiveUdpCommand;
                ClearData();
                IsRunning = false;
                await Log("已断开与服务端的连接");
            }
            else
            {
                // 验证IP和端口格式
                if (string.IsNullOrWhiteSpace(IP) || Port <= 0 || Port > 65535)
                {
                    await Log("请输入有效的IP地址和端口号", LogType.Error);
                    return;
                }

                var (isSuccess, errorMessage) = await TcpClient.ConnectAsync("TCP服务端", IP, Port);
                if (isSuccess)
                {
                    IsRunning = true;
                    await Log("连接服务端成功");
                    StartSendHeartbeat();
                    StartUpdateProcesses(); // 启动UDP数据更新任务
                    await RequestTargetTypeAsync();
                }
                else
                {
                    ClearData();
                    await Log($"连接服务端失败：{errorMessage}", LogType.Error);
                }
            }
        }
        catch (Exception ex)
        {
            await Log($"连接操作失败：{ex.Message}", LogType.Error);
            IsRunning = false;
        }
    }

    private async Task HandleRefreshCommandAsync()
    {
        if (!TcpClient.IsRunning)
        {
            _ = Log("未连接Tcp服务，无法发送命令", LogType.Error);
            return;
        }

        ClearData();
        await TcpClient.SendCommandAsync(new RequestServiceInfo { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求服务基本信息命令");
    }

    private async Task HandleRefreshAllCommandAsync()
    {
        if (!TcpClient.IsRunning)
        {
            Logger.Error("未连接Tcp服务，无法发送命令");
            return;
        }

        await TcpClient.SendCommandAsync(new ChangeProcessList());
        Logger.Info("发送刷新所有客户端命令");
    }

    private CancellationTokenSource? _updateDisplayProcessesToken;
    private CancellationTokenSource? _updateRealtimeProcessListToken;
    private CancellationTokenSource? _updateGeneralProcessListToken;

    private void StartUpdateDisplayProcesses()
    {
        if (_updateDisplayProcessesToken is null || _updateDisplayProcessesToken.IsCancellationRequested)
        {
            _updateDisplayProcessesToken?.Dispose();
            _updateDisplayProcessesToken = new();
        }
        else
        {
            return;
        }

        Task.Factory.StartNew(async () => await UpdateDisplayProcessesAsync(_updateDisplayProcessesToken.Token),
            TaskCreationOptions.LongRunning);
    }

    private void StopUpdateDisplayProcesses()
    {
        if (_updateDisplayProcessesToken is null || _updateDisplayProcessesToken.IsCancellationRequested)
        {
            return;
        }

        _updateDisplayProcessesToken.Cancel();
        try
        {
            _updateDisplayProcessesToken.Token.WaitHandle.WaitOne();
        }
        catch (Exception ex)
        {
            Logger.Error("停止刷新数据异常", ex);
        }
        finally
        {
            _updateDisplayProcessesToken?.Dispose();
            _updateDisplayProcessesToken = null;
        }
    }

    private void StartUpdateProcesses()
    {
        // 启动实时进程更新任务
        if (_updateRealtimeProcessListToken is null || _updateRealtimeProcessListToken.IsCancellationRequested)
        {
            _updateRealtimeProcessListToken?.Dispose();
            _updateRealtimeProcessListToken = new();
        }
        else
        {
            return;
        }

        Task.Factory.StartNew(async () => await UpdateRealtimeProcessListAsync(_updateRealtimeProcessListToken.Token),
            TaskCreationOptions.LongRunning);

        // 启动一般进程更新任务
        if (_updateGeneralProcessListToken is null || _updateGeneralProcessListToken.IsCancellationRequested)
        {
            _updateGeneralProcessListToken?.Dispose();
            _updateGeneralProcessListToken = new();
        }
        else
        {
            return;
        }

        Task.Factory.StartNew(async () => await UpdateGeneralProcessListAsync(_updateGeneralProcessListToken.Token),
            TaskCreationOptions.LongRunning);
    }

    private void StopUpdateProcesses()
    {
        // 停止实时进程更新任务
        if (_updateRealtimeProcessListToken is not null && !_updateRealtimeProcessListToken.IsCancellationRequested)
        {
            _updateRealtimeProcessListToken.Cancel();
            try
            {
                _updateRealtimeProcessListToken.Token.WaitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                Logger.Error("停止实时进程更新异常", ex);
            }
            finally
            {
                _updateRealtimeProcessListToken?.Dispose();
                _updateRealtimeProcessListToken = null;
            }
        }

        // 停止一般进程更新任务
        if (_updateGeneralProcessListToken is not null && !_updateGeneralProcessListToken.IsCancellationRequested)
        {
            _updateGeneralProcessListToken.Cancel();
            try
            {
                _updateGeneralProcessListToken.Token.WaitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                Logger.Error("停止一般进程更新异常", ex);
            }
            finally
            {
                _updateGeneralProcessListToken?.Dispose();
                _updateGeneralProcessListToken = null;
            }
        }
    }

    private async Task UpdateDisplayProcessesAsync(CancellationToken? token)
    {
        // 记录上次更新的进程数量和哈希值，用于检测数据是否真正变化
        int lastProcessCount = -1;
        long lastProcessHash = 0;

        while (token is { IsCancellationRequested: false })
        {
            try
            {
                // 只在数据真正变化时才更新UI
                int currentProcessCount = _tempProcesses.Count;
                long currentProcessHash = CalculateProcessListHash(_tempProcesses);

                if (currentProcessCount != lastProcessCount || currentProcessHash != lastProcessHash)
                {
                    ApplyFilter();
                    lastProcessCount = currentProcessCount;
                    lastProcessHash = currentProcessHash;
                }

                // 动态调整延迟时间，根据进程数量优化性能
                int delayMs = currentProcessCount <= 1000 ? 1000 :
                             currentProcessCount <= 10000 ? 2000 : 3000;

                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token.Value);
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，正常退出循环
                break;
            }
            catch (Exception ex)
            {
                Logger.Error("更新显示进程异常", ex);
                await Task.Delay(TimeSpan.FromMilliseconds(5000), token.Value); // 出现异常时延迟5秒再重试
            }
        }
    }

    /// <summary>
    /// 计算进程列表的哈希值，用于快速检测数据是否变化
    /// </summary>
    private long CalculateProcessListHash(ImmutableList<ProcessItemModel> processes)
    {
        if (processes == null || processes.Count == 0)
        {
            return 0;
        }

        // 使用进程数量和最新进程的哈希值组合计算
        long hash = processes.Count;
        var latestProcess = processes.LastOrDefault();
        if (latestProcess != null)
        {
            hash = hash * 31 + latestProcess.PID;
            hash = hash * 31 + latestProcess.Cpu;
            hash = hash * 31 + latestProcess.Memory;
        }

        return hash;
    }

    private void FilterData(List<ProcessItemModel> processes)
    {
        var currentHashSet = new HashSet<int>(processes.Select(p => p.PID));
        if (_previousProcessHashSets != null && currentHashSet.SetEquals(_previousProcessHashSets))
        {
            return;
        }

        var toAdd = processes.Where(p => _previousProcessHashSets == null || !_previousProcessHashSets.Contains(p.PID))
            .ToList();
        var toRemove = _itemSourceCache.Keys.Where(pid => !currentHashSet.Contains(pid)).ToList();

        var removeCount = toRemove?.Count ?? 0;
        var addCount = toAdd?.Count ?? 0;
        _itemSourceCache.Edit(m =>
        {
            if (removeCount > 0)
            {
                m.RemoveKeys(toRemove);
            }

            if (addCount > 0)
            {
                m.AddOrUpdate(toAdd);
            }
        });

        _previousProcessHashSets = currentHashSet;
    }

    /// <summary>
    ///     应用筛选条件
    /// </summary>
    private void ApplyFilter()
    {
        var query = _tempProcesses.AsParallel();
        if (!string.IsNullOrWhiteSpace(SearchKey))
        {
            query = query.Where(process => !string.IsNullOrWhiteSpace(process.Name) &&
                                           process.Name.Contains(SearchKey, StringComparison.OrdinalIgnoreCase));
        }

        var queryResult = query.ToList();
        FilterData(queryResult);
    }

    private void ClearData()
    {
        ClearTempProcesses();
        _processIdAndItems.Clear();
        _receivedGeneralProcessLists.Clear();
        _receivedRealtimeProcessLists.Clear();
        _previousProcessHashSets = null;
        _itemSourceCache.Clear();
    }

    private void ClearTempProcesses()
    {
        var builder = _tempProcesses.ToBuilder();
        builder.Clear();
        _tempProcesses = builder.ToImmutable();
    }

    private void StartSendHeartbeat()
    {
        StopHeartbeat();
        _sendDataTimer = new Timer();
        _sendDataTimer.Interval = 2000;
        _sendDataTimer.Elapsed += SendHeartbeat;
        _sendDataTimer.Start();
    }

    private void StopHeartbeat()
    {
        _sendDataTimer?.Stop();
        _sendDataTimer?.Dispose();
        _sendDataTimer = null;
    }

    private async void SendHeartbeat(object? sender, ElapsedEventArgs e)
    {
        try
        {
            if (!TcpClient.IsRunning) return;

            await TcpClient.SendCommandAsync(new Heartbeat() { TaskId = NetHelper.GetTaskId() });
        }
        catch (Exception ex)
        {
            Logger.Error("发送心跳包时发生异常", ex, "发送心跳包时发生异常，详细信息请查看日志文件");
        }
    }

    private void Try(string actionName, Action action, Action<Exception>? exceptionAction = null)
    {
        try
        {
            action.Invoke();
        }
        catch (Exception ex)
        {
            if (exceptionAction == null)
                Logger.Error($"执行{actionName}异常：{ex.Message}");
            else
                exceptionAction.Invoke(ex);
        }
    }

    private void Invoke(Action action)
    {
        Dispatcher.UIThread.Post(action.Invoke);
    }

    private async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action.Invoke);
    }


    #region 接收事件

    private void ReceiveTcpData()
    {
        // 开启线程接收数据
        Task.Run(async () =>
        {
            while (!TcpClient.IsRunning) await Task.Delay(TimeSpan.FromMilliseconds(10));

            await HandleRefreshCommandAsync();
        });
    }

    /// <summary>
    /// 处理接收的Socket消息
    /// </summary>
    /// <param name="message"></param>
    /// <exception cref="Exception"></exception>
    [EventHandler]
    private async Task ReceivedSocketCommandAsync(SocketCommand command)
    {
        if (command.IsCommand<ResponseTargetType>())
        {
            await ReceivedSocketCommandAsync(command.GetCommand<ResponseTargetType>());
        }
        else if (command.IsCommand<ResponseUdpAddress>())
        {
            await ReceivedSocketCommandAsync(command.GetCommand<ResponseUdpAddress>());
        }
        else if (command.IsCommand<ResponseServiceInfo>())
        {
            await ReceivedSocketCommandAsync(command.GetCommand<ResponseServiceInfo>());
        }
        else if (command.IsCommand<ResponseProcessIDList>())
        {
            await ReceivedSocketCommandAsync(command.GetCommand<ResponseProcessIDList>());
        }
        else if (command.IsCommand<ResponseProcessList>())
        {
            await ReceivedSocketCommandAsync(command.GetCommand<ResponseProcessList>());
        }
        else if (command.IsCommand<UpdateProcessList>())
        {
            await ReceivedSocketCommandAsync(command.GetCommand<UpdateProcessList>());
        }
        else if (command.IsCommand<ChangeProcessList>())
        {
            await HandleRefreshCommandAsync();
        }
        else if (command.IsCommand<Heartbeat>())
        {
            ReceivedSocketCommand(command.GetCommand<Heartbeat>());
        }
    }

    private void ReceiveUdpCommand(object? sender, SocketCommand? command)
    {
        if (command.IsCommand<UpdateRealtimeProcessList>())
        {
            ReceivedSocketCommand(command.GetCommand<UpdateRealtimeProcessList>());
        }
        else if (command.IsCommand<UpdateGeneralProcessList>())
        {
            ReceivedSocketCommand(command.GetCommand<UpdateGeneralProcessList>());
        }
    }

    private async Task ReceivedSocketCommandAsync(ResponseTargetType response)
    {
        var type = (TerminalType)Enum.Parse(typeof(TerminalType), response.Type.ToString());
        if (response.Type == (byte)TerminalType.Server)
        {
            _ = Log($"正确连接{type.GetDescription()}，程序正常运行");

            await TcpClient.SendCommandAsync(new RequestUdpAddress());
            _ = Log("发送命令获取Udp组播地址");

            await HandleRefreshCommandAsync();
            StartUpdateDisplayProcesses();
        }
        else
        {
            _ = Log($"目标终端非服务端(type: {type.GetDescription()})，请检查地址是否配置正确（重点检查端口），即将断开连接", LogType.Error);
        }
    }

    private async Task ReceivedSocketCommandAsync(ResponseUdpAddress response)
    {
        _ = Log($"收到Udp组播地址=》{response.Ip}:{response.Port}");

        await UdpClient.ConnectAsync("UDP组播", response.Ip, response.Port, TcpClient.LocalEndPoint,
            TcpClient.SystemId);
        UdpClient.Received -= ReceiveUdpCommand;
        UdpClient.Received += ReceiveUdpCommand;
        _ = Log("尝试订阅Udp组播");
    }

    private async Task ReceivedSocketCommandAsync(ResponseServiceInfo response)
    {
        _timestampStartYear = response.TimestampStartYear;
        var oldBaseInfo = BaseInfo;
        BaseInfo =
            $"更新时间【{response.LastUpdateTime.FromSpecialUnixTimeSecondsToDateTime(response.TimestampStartYear):yyyy:MM:dd HH:mm:ss fff}】：操作系统【{response.OS}】-内存【{response.MemorySize}GB】-处理器【{response.ProcessorCount}个】-硬盘【{response.DiskSize}GB】-带宽【{response.NetworkBandwidth}Mbps】";

        Logger.Info(response.TaskId == default ? "收到服务端推送的基本信息" : "收到请求基本信息响应");
        Logger.Info($"【旧】{oldBaseInfo}");
        Logger.Info($"【新】{BaseInfo}");
        _ = Log(BaseInfo);

        await TcpClient.SendCommandAsync(new RequestProcessIDList() { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求进程ID列表命令");

        ClearData();
    }

    private async Task ReceivedSocketCommandAsync(ResponseProcessIDList response)
    {
        _processIdArray = response.IDList!;
        _ = Log($"收到进程ID列表，共{_processIdArray.Length}个进程");

        await TcpClient.SendCommandAsync(new RequestProcessList { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求进程详细信息列表命令");
    }

    private async Task ReceivedSocketCommandAsync(ResponseProcessList response)
    {
        var processes =
            response.Processes?.Select(process => new ProcessItemModel(process, _timestampStartYear)).ToList();
        var builder = _tempProcesses.ToBuilder();
        builder.AddRange(processes!);
        _tempProcesses = builder.ToImmutable();
        processes.ForEach(process => { _processIdAndItems[process.PID] = process; });

        var msg = response.TaskId == default ? "收到推送" : "收到请求响应";
        Logger.Info(
            $"{msg}【{response.PageIndex + 1}/{response.PageCount}】进程{processes.Count}条({_tempProcesses.Count}/{response.TotalSize})"
        );
    }

    private async Task ReceivedSocketCommandAsync(UpdateProcessList response)
    {
        if (_processIdAndItems == null) return;

        response.Processes?.ForEach(updateProcess =>
        {
            if (_processIdAndItems.TryGetValue(updateProcess.Pid, out var point))
                point.Update(updateProcess, _timestampStartYear);
            else
                throw new Exception($"收到更新数据包，遇到本地缓存不存在的进程：{updateProcess.Name}");
        });
        Logger.Info($"更新数据{response.Processes?.Count}条");
    }

    private void ReceivedSocketCommand(Heartbeat response)
    {
    }

    #endregion

    #region 接收Udp数据

    private void ReceivedSocketCommand(UpdateRealtimeProcessList response)
    {
        _receivedRealtimeProcessLists[response.PageIndex] = response;
    }


    private void ReceivedSocketCommand(UpdateGeneralProcessList response)
    {
        _receivedGeneralProcessLists[response.PageIndex] = response;
    }

    private const int _dillRealtimeDuration = 500;

    private async Task UpdateRealtimeProcessListAsync(CancellationToken token)
    {
        try
        {
            // 持续处理接收到的UDP数据
            while (!token.IsCancellationRequested)
            {
                if (_receivedRealtimeProcessLists.Count > 0)
                {
                    // 处理当前接收到的所有响应
                    foreach (var response in _receivedRealtimeProcessLists.Values.ToList())
                    {
                        UpdateRealtimeProcessList(response);
                    }

                    // 清空已处理的响应
                    _receivedRealtimeProcessLists.Clear();
                }
                await Task.Delay(TimeSpan.FromMilliseconds(_dillRealtimeDuration), token);
            }
        }
        catch (OperationCanceledException)
        {
            // 任务被取消，正常退出
        }
        catch (Exception ex)
        {
            Logger.Error($"处理实时进程列表更新异常：{ex.Message}");
        }
    }

    private void UpdateRealtimeProcessList()
    {
        // 此方法已被UpdateRealtimeProcessListAsync替代
    }

    private void UpdateRealtimeProcessList(UpdateRealtimeProcessList response)
    {
        void LogNotExistProcess(int index)
        {
            Console.WriteLine($"【实时】收到更新数据包，遇到本地缓存不存在的进程，索引：{index}");
        }

        try
        {
            var startIndex = response.PageIndex * response.PageSize;
            var dataCount = response.Cpus.Length / 2;

            // 批量更新所有进程，减少UI通知次数
            var batchUpdatedProcesses = new List<ProcessItemModel>();

            for (var i = 0; i < dataCount; i++)
            {
                if (_processIdArray?.Length > startIndex && _processIdAndItems?.Count > startIndex)
                {
                    var processId = _processIdArray[startIndex];
                    if (_processIdAndItems.TryGetValue(processId, out var process))
                    {
                        // 开始批量更新
                        process.BeginUpdate();
                        batchUpdatedProcesses.Add(process);

                        var cpu = BitConverter.ToInt16(response.Cpus, i * sizeof(Int16));
                        var memory = BitConverter.ToInt16(response.Memories, i * sizeof(Int16));
                        var disk = BitConverter.ToInt16(response.Disks, i * sizeof(Int16));
                        var network = BitConverter.ToInt16(response.Networks, i * sizeof(Int16));
                        process.Update(cpu, memory, disk, network);
                    }
                    else
                    {
                        LogNotExistProcess(startIndex);
                    }
                }
                else
                {
                    LogNotExistProcess(startIndex);
                }

                startIndex++;
            }

            // 结束所有进程的批量更新
            foreach (var process in batchUpdatedProcesses)
            {
                process.EndUpdate();
            }

            // 减少实时日志输出频率，只在调试时使用
            // Console.WriteLine($"【实时】更新数据{dataCount}条");
        }
        catch (Exception ex)
        {
            Logger.Error($"【实时】更新数据异常：{ex.Message}");
        }
    }

    private const int _dillGeneralDuration = 1000;

    private async Task UpdateGeneralProcessListAsync(CancellationToken token)
    {
        try
        {
            // 持续处理接收到的UDP数据
            while (!token.IsCancellationRequested)
            {
                if (_receivedGeneralProcessLists.Count > 0)
                {
                    // 处理当前接收到的所有响应
                    foreach (var response in _receivedGeneralProcessLists.Values.ToList())
                    {
                        UpdateGeneralProcessList(response);
                    }

                    // 清空已处理的响应
                    _receivedGeneralProcessLists.Clear();
                }
                await Task.Delay(TimeSpan.FromMilliseconds(_dillGeneralDuration), token);
            }
        }
        catch (OperationCanceledException)
        {
            // 任务被取消，正常退出
        }
        catch (Exception ex)
        {
            Logger.Error($"处理一般进程列表更新异常：{ex.Message}");
        }
    }

    private void UpdateGeneralProcessList()
    {
        // 此方法已被UpdateGeneralProcessListAsync替代
    }

    private void UpdateGeneralProcessList(UpdateGeneralProcessList response)
    {
        void LogNotExistProcess(int index)
        {
            Console.WriteLine($"【实时】收到更新一般数据包，遇到本地缓存不存在的进程，索引：{index}");
        }

        try
        {
            var startIndex = response.PageIndex * response.PageSize;
            var dataCount = response.ProcessStatuses.Length;

            // 批量更新所有进程，减少UI通知次数
            var batchUpdatedProcesses = new List<ProcessItemModel>();

            for (var i = 0; i < dataCount; i++)
            {
                if (_processIdArray?.Length > startIndex && _processIdAndItems?.Count > startIndex)
                {
                    var processId = _processIdArray[startIndex];
                    if (_processIdAndItems.TryGetValue(processId, out var process))
                    {
                        // 开始批量更新
                        process.BeginUpdate();
                        batchUpdatedProcesses.Add(process);

                        var processStatus = response.ProcessStatuses[i];
                        var alarmStatus = response.AlarmStatuses[i];
                        var gpu = BitConverter.ToInt16(response.Gpus, i * sizeof(short));
                        var gpuEngine = response.GpuEngine[i];
                        var powerUsage = response.PowerUsage[i];
                        var powerUsageTrend = response.PowerUsageTrend[i];
                        var updateTime = BitConverter.ToUInt32(response.UpdateTimes, i * sizeof(uint));
                        process.Update(_timestampStartYear, processStatus, alarmStatus, gpu, gpuEngine, powerUsage,
                            powerUsageTrend, updateTime);
                    }
                    else
                    {
                        LogNotExistProcess(startIndex);
                    }
                }
                else
                {
                    LogNotExistProcess(startIndex);
                }

                startIndex++;
            }

            // 结束所有进程的批量更新
            foreach (var process in batchUpdatedProcesses)
            {
                process.EndUpdate();
            }

            // 减少实时日志输出频率，只在调试时使用
            // Console.WriteLine($"【实时】更新一般数据{dataCount}条");
        }
        catch (Exception ex)
        {
            Logger.Error($"【实时】更新一般数据异常：{ex.Message}");
        }
    }

    #endregion

    #region Socket命令发送

    private async Task RequestTargetTypeAsync()
    {
        await TcpClient.SendCommandAsync(new RequestTargetType() { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送命令查询目标终端类型是否是服务端");
    }

    private async Task ReceiveUdpStatusMessage()
    {
        _ = Log("Udp组播订阅成功！");
    }

    #endregion Socket命令发送

    private async Task Log(string msg, LogType type = LogType.Info, bool showNotification = false)
    {
        if (type == LogType.Info)
        {
            Logger.Info(msg);
        }
        else if (type == LogType.Error)
        {
            Logger.Error(msg);
        }

        await ShowNotificationAsync(showNotification, msg, type);
    }

    private async Task ShowNotificationAsync(bool showNotification, string msg, LogType type)
    {
        if (!showNotification) return;

        var notificationType = type switch
        {
            LogType.Warn => NotificationType.Warning,
            LogType.Error => NotificationType.Error,
            _ => NotificationType.Information
        };

        await InvokeAsync(() => NotificationManager?.Show(new Notification(title: "提示", msg, notificationType)));
    }
}