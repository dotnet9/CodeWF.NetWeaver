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
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Timers;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace SocketTest.Client.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public WindowNotificationManager? NotificationManager { get; set; }
    private readonly List<ProcessItemModel> _receivedProcesses = new();
    private TcpSocketClient _tcpClient { get; set; } = new();
    private UdpSocketClient _udpClient { get; set; } = new();


    private int[]? _processIdArray;
    private Dictionary<int, ProcessItemModel>? _processIdAndItems;

    private string? _searchKey;

    /// <summary>
    ///     搜索关键词
    /// </summary>
    public string? SearchKey
    {
        get => _searchKey;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchKey, value);
            ApplyFilter();
        }
    }

    private Timer? _sendDataTimer;
    private int _timestampStartYear;

    public MainWindowViewModel()
    {
        DisplayProcesses = new();

        void RegisterCommand()
        {
            RefreshCommand = ReactiveCommand.CreateFromTask(HandleRefreshCommandAsync);
            RefreshAllCommand = ReactiveCommand.CreateFromTask(HandleRefreshAllCommandAsync);
            ConnectCommand = ReactiveCommand.CreateFromTask(HandleConnectTcpCommandAsync);
        }

        EventBus.Default.Subscribe(this);
        RegisterCommand();

        Logger.Info("连接服务端后获取数据");
    }

    #region 属性

    public Window? Owner { get; set; }
    public RangObservableCollection<ProcessItemModel> DisplayProcesses { get; }

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
                _tcpClient.Stop();
                _udpClient.Stop();

                _udpClient.Received -= ReceiveUdpCommand;
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

                var (isSuccess, errorMessage) = await _tcpClient.ConnectAsync("TCP服务端", IP, Port);
                if (isSuccess)
                {
                    IsRunning = true;
                    await Log("连接服务端成功");
                    StartSendHeartbeat();
                    await RequestTargetTypeAsync();
                }
                else
                {
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
        if (!_tcpClient.IsRunning)
        {
            _ = Log("未连接Tcp服务，无法发送命令", LogType.Error);
            return;
        }

        ClearData();
        await _tcpClient.SendCommandAsync(new RequestServiceInfo { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求服务基本信息命令");
    }

    private async Task HandleRefreshAllCommandAsync()
    {
        if (!_tcpClient.IsRunning)
        {
            Logger.Error("未连接Tcp服务，无法发送命令");
            return;
        }

        await _tcpClient.SendCommandAsync(new ChangeProcessList());
        Logger.Info("发送刷新所有客户端命令");
    }

    private IEnumerable<ProcessItemModel> FilterData(IEnumerable<ProcessItemModel> processes)
    {
        return string.IsNullOrWhiteSpace(_searchKey)
            ? processes
            : processes.Where(process =>
                !string.IsNullOrWhiteSpace(process.Name) &&
                process.Name.Contains(_searchKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     应用筛选条件
    /// </summary>
    private void ApplyFilter()
    {
        var filteredProcesses = FilterData(_receivedProcesses);
        Invoke(() =>
        {
            DisplayProcesses.Clear();
            DisplayProcesses.AddRange(filteredProcesses);
        });
    }

    private void ClearData()
    {
        _receivedProcesses.Clear();
        Invoke(DisplayProcesses.Clear);
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
            if (!_tcpClient.IsRunning) return;

            await _tcpClient.SendCommandAsync(new Heartbeat() { TaskId = NetHelper.GetTaskId() });
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
            while (!_tcpClient.IsRunning) await Task.Delay(TimeSpan.FromMilliseconds(10));

            await HandleRefreshCommandAsync();
        });
    }

    /// <summary>
    /// 处理接收的Socket消息
    /// </summary>
    /// <param name="message"></param>
    /// <exception cref="Exception"></exception>
    [EventHandler]
    private async Task ReceivedSocketCommandAsync(SocketCommand message)
    {
        if (message.IsCommand<ResponseTargetType>())
        {
            await ReceivedSocketCommandAsync(message.GetCommand<ResponseTargetType>());
        }
        else if (message.IsCommand<ResponseUdpAddress>())
        {
            await ReceivedSocketCommandAsync(message.GetCommand<ResponseUdpAddress>());
        }
        else if (message.IsCommand<ResponseServiceInfo>())
        {
            await ReceivedSocketCommandAsync(message.GetCommand<ResponseServiceInfo>());
        }
        else if (message.IsCommand<ResponseProcessIDList>())
        {
            await ReceivedSocketCommandAsync(message.GetCommand<ResponseProcessIDList>());
        }
        else if (message.IsCommand<ResponseProcessList>())
        {
            await ReceivedSocketCommandAsync(message.GetCommand<ResponseProcessList>());
        }
        else if (message.IsCommand<UpdateProcessList>())
        {
            await ReceivedSocketCommandAsync(message.GetCommand<UpdateProcessList>());
        }
        else if (message.IsCommand<ChangeProcessList>())
        {
            await HandleRefreshCommandAsync();
        }
        else if (message.IsCommand<Heartbeat>())
        {
            ReceivedSocketCommand(message.GetCommand<Heartbeat>());
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

            await _tcpClient.SendCommandAsync(new RequestUdpAddress());
            _ = Log("发送命令获取Udp组播地址");

            await HandleRefreshCommandAsync();
        }
        else
        {
            _ = Log($"目标终端非服务端(type: {type.GetDescription()})，请检查地址是否配置正确（重点检查端口），即将断开连接", LogType.Error);
        }
    }

    private async Task ReceivedSocketCommandAsync(ResponseUdpAddress response)
    {
        _ = Log($"收到Udp组播地址=》{response.Ip}:{response.Port}");

        await _udpClient.ConnectAsync("UDP组播", response.Ip, response.Port, _tcpClient.LocalEndPoint,
            _tcpClient.SystemId);
        _udpClient.Received -= ReceiveUdpCommand;
        _udpClient.Received += ReceiveUdpCommand;
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

        await _tcpClient.SendCommandAsync(new RequestProcessIDList() { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求进程ID列表命令");

        ClearData();
    }

    private async Task ReceivedSocketCommandAsync(ResponseProcessIDList response)
    {
        _processIdArray = response.IDList!;
        _ = Log($"收到进程ID列表，共{_processIdArray.Length}个进程");

        await _tcpClient.SendCommandAsync(new RequestProcessList { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求进程详细信息列表命令");
    }

    private async Task ReceivedSocketCommandAsync(ResponseProcessList response)
    {
        // 将耗时的数据处理操作放到后台线程中执行
        await Task.Run(() =>
        {
            var processes =
                response.Processes?.Select(process => new ProcessItemModel(process, _timestampStartYear)).ToList();
            if (!(processes?.Count > 0)) return;

            // 先更新后台数据
            lock (_receivedProcesses) // 确保线程安全
            {
                _receivedProcesses.AddRange(processes);
            }

            var filterData = FilterData(processes);

            // 只在需要更新UI时才调用Invoke
            Invoke(() => DisplayProcesses.AddRange(filterData));

            // 当收到全部数据时构建字典，这个操作也比较耗时，放到后台线程
            if (_receivedProcesses.Count == response.TotalSize)
            {
                lock (_receivedProcesses) // 确保线程安全
                {
                    _processIdAndItems = _receivedProcesses.ToDictionary(process => process.PID);
                }
            }

            var msg = response.TaskId == default ? "收到推送" : "收到请求响应";
            Logger.Info(
                $"{msg}【{response.PageIndex + 1}/{response.PageCount}】进程{processes.Count}条({_receivedProcesses.Count}/{response.TotalSize})"
            );
        });
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
        void LogNotExistProcess(int index)
        {
            Console.WriteLine($"【实时】收到更新数据包，遇到本地缓存不存在的进程，索引：{index}");
        }

        try
        {
            var startIndex = response.PageIndex * response.PageSize;
            var dataCount = response.Cpus.Length / 2;
            for (var i = 0; i < dataCount; i++)
            {
                if (_processIdArray?.Length > startIndex && _processIdAndItems?.Count > startIndex)
                {
                    var processId = _processIdArray[startIndex];
                    if (_processIdAndItems.TryGetValue(processId, out var process))
                    {
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

            // 减少实时日志输出频率，只在调试时使用
            // Console.WriteLine($"【实时】更新数据{dataCount}条");
        }
        catch (Exception ex)
        {
            Logger.Error($"【实时】更新数据异常：{ex.Message}");
        }
    }

    private void ReceivedSocketCommand(UpdateGeneralProcessList response)
    {
        void LogNotExistProcess(int index)
        {
            Console.WriteLine($"【实时】收到更新一般数据包，遇到本地缓存不存在的进程，索引：{index}");
        }

        try
        {
            var startIndex = response.PageIndex * response.PageSize;
            var dataCount = response.ProcessStatuses.Length;
            for (var i = 0; i < dataCount; i++)
            {
                if (_processIdArray?.Length > startIndex && _processIdAndItems?.Count > startIndex)
                {
                    var processId = _processIdArray[startIndex];
                    if (_processIdAndItems.TryGetValue(processId, out var process))
                    {
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
        await _tcpClient.SendCommandAsync(new RequestTargetType() { TaskId = NetHelper.GetTaskId() });
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