using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWeaver;
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
using SocketTest.Server.Mock;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reactive;
using System.Threading.Tasks;
using System.Timers;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace SocketTest.Server.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public WindowNotificationManager? NotificationManager { get; set; }
    private TcpSocketServer _tcpServer { get; set; }
    private UdpSocketServer _udpServer { get; set; }


    public MainWindowViewModel()
    {
        void RegisterCommand()
        {
            // 仍然使用WhenAnyValue来创建基于IsRunning状态的命令
            RunCommand = ReactiveCommand.CreateFromTask(HandleRunCommandCommandAsync);
            RefreshCommand = ReactiveCommand.CreateFromTask(HandleRefreshCommandAsync);
            UpdateCommand = ReactiveCommand.CreateFromTask(HandleUpdateCommandAsync);
        }

        _tcpServer = new TcpSocketServer()
        {
            SystemId = DateTime.Now.Ticks
        };
        _udpServer = new UdpSocketServer();

        EventBus.Default.Subscribe(this);
        RegisterCommand();

        MockUpdate();
        MockSendData();

        Logger.Info("连接服务端后获取数据");
    }

    #region 属性

    public string? TCPIP
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "127.0.0.1";

    public int TCPPort
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 5000;

    public string? UDPIP
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "224.0.0.0";

    public int UDPPort
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 9540;

    public bool IsRunning
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }


    /// <summary>
    ///     连接的客户端数量
    /// </summary>
    public int ClientCount
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 0;

    /// <summary>
    ///     TCP服务状态
    /// </summary>
    public string TCPStatus
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "TCP服务未运行";

    /// <summary>
    ///     UDP组播状态
    /// </summary>
    public string UDPStatus
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "UDP组播未运行";

    /// <summary>
    ///     发送的数据包数量
    /// </summary>
    public int SentDataCount
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 0;

    /// <summary>
    ///     模拟数据总量
    /// </summary>
    public int MockCount
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 200000;

    /// <summary>
    ///     模拟分包数据量
    /// </summary>
    public int MockPageSize
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 5000;

    /// <summary>
    ///     刷新数据
    /// </summary>
    public ReactiveCommand<Unit, Unit>? RefreshCommand { get; private set; }

    /// <summary>
    ///     更新数据
    /// </summary>
    public ReactiveCommand<Unit, Unit>? UpdateCommand { get; private set; }

    /// <summary>
    ///     运行命令
    /// </summary>
    public ReactiveCommand<Unit, Unit>? RunCommand { get; private set; }

    #endregion 属性

    public async Task HandleRunCommandCommandAsync()
    {
        try
        {
            if (!_tcpServer.IsRunning)
            {
                // 验证IP和端口格式
                if (string.IsNullOrWhiteSpace(TCPIP) || TCPPort <= 0 || TCPPort > 65535)
                {
                    await Log("请输入有效的IP地址和端口号", LogType.Error);
                    return;
                }

                var (isSuccess, errorMessage) = await _tcpServer.StartAsync("TCP服务端", TCPIP, TCPPort);
                if (isSuccess)
                {
                    TCPStatus = "TCP服务已启动";
                    IsRunning = true;
                    await Log("TCP服务已启动");
                    (isSuccess, errorMessage) = _udpServer.Start("UDP服务端", _tcpServer.SystemId, UDPIP, UDPPort);
                    if (isSuccess)
                    {
                        await Log("UDP组播已启动");
                        await SendMockDataAsync();
                    }
                    else
                    {
                        await Log($"UDP组播启动失败：{errorMessage}", LogType.Error);
                    }
                }
                else
                {
                    TCPStatus = "TCP服务启动失败";
                    await Log($"TCP服务启动失败：{errorMessage}", LogType.Error);
                }
            }
            else
            {
                await _tcpServer.StopAsync();
                _udpServer.Stop();
                TCPStatus = "TCP服务已停止";
                UDPStatus = "UDP组播已停止";
                ClientCount = 0;
                IsRunning = false;
                await Log("服务已停止");
            }
        }
        catch (Exception ex)
        {
            IsRunning = false;
            await Log($"操作失败：{ex.Message}", LogType.Error);
        }
    }

    private async Task HandleRefreshCommandAsync()
    {
        if (!_tcpServer.IsRunning)
        {
            Logger.Error("未运行Tcp服务，无法发送命令");
            return;
        }

        UpdateAllData(true);
    }

    private async Task HandleUpdateCommandAsync()
    {
        if (!_tcpServer.IsRunning)
        {
            Logger.Error("未运行Tcp服务，无法发送命令");
            return;
        }

        UpdateAllData(false);
    }

    #region 处理Socket信息

    [EventHandler]
    private async Task ReceiveSocketCommandAsync(SocketCommand message)
    {
        if (message.IsCommand<RequestTargetType>())
        {
            await ReceiveSocketCommandAsync(message.Client!, message.GetCommand<RequestTargetType>());
        }
        else if (message.IsCommand<RequestUdpAddress>())
        {
            await ReceiveSocketCommandAsync(message.Client!, message.GetCommand<RequestUdpAddress>());
        }
        else if (message.IsCommand<RequestServiceInfo>())
        {
            await ReceiveSocketCommandAsync(message.Client!, message.GetCommand<RequestServiceInfo>());
        }
        else if (message.IsCommand<RequestProcessIDList>())
        {
            await ReceiveSocketCommandAsync(message.Client!, message.GetCommand<RequestProcessIDList>());
        }
        else if (message.IsCommand<RequestProcessList>())
        {
            await ReceiveSocketCommandAsync(message.Client!, message.GetCommand<RequestProcessList>());
        }
        else if (message.IsCommand<ChangeProcessList>())
        {
            await ReceiveSocketCommandAsync(message.Client!, message.GetCommand<ChangeProcessList>());
        }
        else if (message.IsCommand<Heartbeat>())
        {
            await ReceiveSocketCommandAsync(message.Client!, message.GetCommand<Heartbeat>());
        }
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestTargetType request)
    {
        _ = Log("收到请求终端类型命令");
        var currentTerminalType = TerminalType.Server;

        var response = new ResponseTargetType()
        {
            TaskId = request.TaskId,
            Type = (byte)currentTerminalType
        };
        await _tcpServer.SendCommandAsync(client, response);

        _ = Log($"响应请求终端类型命令：当前终端为={currentTerminalType.GetDescription()}");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestUdpAddress request)
    {
        _ = Log("收到请求Udp组播地址命令");

        var response = new ResponseUdpAddress()
        {
            TaskId = request.TaskId,
            Ip = _udpServer.ServerIP,
            Port = _udpServer.ServerPort,
        };
        await _tcpServer.SendCommandAsync(client, response);

        _ = Log($"响应请求Udp组播地址命令：{response.Ip}:{response.Port}");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestServiceInfo request)
    {
        _ = Log("收到请求基本信息命令");

        var data = MockUtil.GetBaseInfoAsync().Result!;
        data.TaskId = request.TaskId;
        await _tcpServer.SendCommandAsync(client, data);

        _ = Log($"响应基本信息命令：当前操作系统版本号={data.OS}，内存大小={data.MemorySize}GB");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessIDList request)
    {
        _ = Log("收到请求进程ID列表命令");

        var response = new ResponseProcessIDList()
        {
            TaskId = request.TaskId,
            IDList = MockUtil.GetProcessIdListAsync().Result
        };
        await _tcpServer.SendCommandAsync(client, response);

        _ = Log($"响应进程ID列表命令：{response.IDList?.Length}");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessList request)
    {
        _ = Log("收到请求进程详细信息列表命令");
        await Task.Run(async () =>
        {
            var pageCount = MockUtil.GetPageCount(MockCount, MockPageSize);
            var sendCount = 0;
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var response = new ResponseProcessList
                {
                    TaskId = request.TaskId,
                    TotalSize = MockCount,
                    PageSize = MockPageSize,
                    PageCount = pageCount,
                    PageIndex = pageIndex,
                    Processes = await MockUtil.MockProcessesAsync(MockPageSize, pageIndex)
                };
                sendCount += response.Processes.Count;
                await _tcpServer.SendCommandAsync(client, response);
                await Task.Delay(TimeSpan.FromMilliseconds(10));

                var msg = response.TaskId == default ? "推送" : "响应请求";
                Logger.Info(
                    $"{msg}【{response.PageIndex + 1}/{response.PageCount}】{response.Processes.Count}条({sendCount}/{response.TotalSize})");
            }
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, ChangeProcessList changeProcess)
    {
        await _tcpServer.SendCommandAsync(changeProcess);
    }

    private async Task ReceiveSocketCommandAsync(Socket client, Heartbeat heartbeat)
    {
        await _tcpServer.SendCommandAsync(client, heartbeat);
        _tcpServer.HeartbeatTime = DateTime.Now;
    }

    #endregion


    #region 更新数据

    private Timer? _sendDataTimer;
    private bool _isUpdateAll;

    public void UpdateAllData(bool isUpdateAll)
    {
        _isUpdateAll = isUpdateAll;
        MockSendData(null, null);
    }

    private void MockUpdate()
    {
        _sendDataTimer = new Timer();
        _sendDataTimer.Interval = 4 * 60 * 1000;
        _sendDataTimer.Elapsed += MockSendData;
        _sendDataTimer.Start();
    }

    private async void MockSendData(object? sender, ElapsedEventArgs? e)
    {
        if (_isUpdateAll)
        {
            _tcpServer.SendCommandAsync(new ChangeProcessList());
            Logger.Info("====TCP推送结构变化通知====");
            return;
        }

        _tcpServer.SendCommandAsync(new UpdateProcessList
        {
            Processes = await MockUtil.MockProcessesAsync(MockCount, MockPageSize)
        });
        Logger.Info("====TCP推送更新通知====");

        _isUpdateAll = !_isUpdateAll;
    }

    #endregion


    #region 模拟数据更新

    private Timer? _updateDataTimer;
    private Timer? _sendRealtimeDataTimer;
    private Timer? _sendGeneralDataTimer;

    private void MockSendData()
    {
        _updateDataTimer = new Timer();
        _updateDataTimer.Interval = MockConst.UdpUpdateMilliseconds;
        _updateDataTimer.Elapsed += MockUpdateDataAsync;
        _updateDataTimer.Start();

        _sendRealtimeDataTimer = new Timer();
        _sendRealtimeDataTimer.Interval = MockConst.UdpSendRealtimeMilliseconds;
        _sendRealtimeDataTimer.Elapsed += MockSendRealtimeDataAsync;
        _sendRealtimeDataTimer.Start();

        _sendGeneralDataTimer = new Timer();
        _sendGeneralDataTimer.Interval = MockConst.UdpSendGeneralMilliseconds;
        _sendGeneralDataTimer.Elapsed += MockSendGeneralDataAsync;
        _sendGeneralDataTimer.Start();
    }

    private async void MockUpdateDataAsync(object? sender, ElapsedEventArgs e)
    {
        if (!_udpServer.IsRunning || !MockUtil.IsInitOver) return;

        var sw = Stopwatch.StartNew();

        await MockUtil.MockUpdateDataAsync();

        sw.Stop();

        Logger.Info($"更新模拟实时数据{sw.ElapsedMilliseconds}ms");
    }

    private async void MockSendRealtimeDataAsync(object? sender, ElapsedEventArgs e)
    {
        if (!_udpServer.IsRunning || !MockUtil.IsInitOver) return;

        var sw = Stopwatch.StartNew();

        MockUtil.MockUpdateRealtimeProcessPageCount(MockCount, SerializeHelper.MaxUdpPacketSize, out var pageSize,
            out var pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            if (!_udpServer.IsRunning) break;

            var response = MockUtil.MockUpdateRealtimeProcessList(pageSize, pageIndex);
            response.TotalSize = MockCount;
            response.PageSize = pageSize;
            response.PageCount = pageCount;
            response.PageIndex = pageIndex;

            await _udpServer.SendCommandAsync(response, DateTimeOffset.UtcNow);
        }

        Logger.Info(
            $"推送实时数据{MockCount}条，单包{pageSize}条分{pageCount}包，{sw.ElapsedMilliseconds}ms");
    }

    private async void MockSendGeneralDataAsync(object? sender, ElapsedEventArgs e)
    {
        if (!_udpServer.IsRunning || !MockUtil.IsInitOver) return;

        var sw = Stopwatch.StartNew();

        MockUtil.MockUpdateGeneralProcessPageCount(MockCount, SerializeHelper.MaxUdpPacketSize, out var pageSize,
            out var pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            if (!_udpServer.IsRunning) break;

            var response = MockUtil.MockUpdateGeneralProcessList(pageSize, pageIndex);
            response.TotalSize = MockCount;
            response.PageSize = pageSize;
            response.PageCount = pageCount;
            response.PageIndex = pageIndex;

            await _udpServer.SendCommandAsync(response, DateTimeOffset.UtcNow);
        }

        Logger.Info(
            $"推送一般数据{MockCount}条，单包{pageSize}条分{pageCount}包，{sw.ElapsedMilliseconds}ms");
    }

    #endregion

    #region Socket命令处理

    [EventHandler]
    private async Task SendMockDataAsync()
    {
        _ = Log(_tcpServer.IsRunning ? "TCP服务已运行" : "TCP服务已停止");
        if (_tcpServer.IsRunning)
        {
            await Task.Run(async () =>
            {
                await MockUtil.MockAsync(MockCount);
                _ = Log("数据模拟完成，客户端可以正常请求数据了");
            });
        }
    }

    #endregion Socket命令处理

    private void Invoke(Action action)
    {
        Dispatcher.UIThread.Post(action.Invoke);
    }

    private async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action.Invoke);
    }

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