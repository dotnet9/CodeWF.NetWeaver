using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using SocketTest.Server.Dtos;
using Timer = System.Timers.Timer;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace SocketTest.Server.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public WindowNotificationManager? NotificationManager { get; set; }
    private readonly List<string> _selectedFilePaths = new();
    private CancellationTokenSource? _uploadCancellation;

    public MainWindowViewModel()
    {
        FileTransferList = new ObservableCollection<FileTransferItem>();

        void RegisterCommand()
        {
            RefreshCommand = ReactiveCommand.CreateFromTask(HandleRefreshCommandAsync);
            UpdateCommand = ReactiveCommand.CreateFromTask(HandleUpdateCommandAsync);
            SelectFilesCommand = ReactiveCommand.CreateFromTask(HandleSelectFilesAsync);
            UploadFilesCommand = ReactiveCommand.CreateFromTask(HandleUploadFilesAsync);
            DownloadFilesCommand = ReactiveCommand.CreateFromTask(HandleDownloadFilesAsync);
            CancelTransferCommand = ReactiveCommand.Create(HandleCancelTransfer);
        }

        TcpHelper = new TcpSocketServer();
        UdpHelper = new UdpSocketServer();

        TcpHelper.FileTransferProgress += OnFileTransferProgress;

        EventBus.Default.Subscribe(this);
        RegisterCommand();

        MockUpdate();
        MockSendData();

        Logger.Info("连接服务端后获取数据");
    }

    #region 属性

    public TcpSocketServer TcpHelper { get; set; }
    public UdpSocketServer UdpHelper { get; set; }

    public ObservableCollection<KeyValuePair<string, Socket>> ConnectedClients { get; } = new();
    public KeyValuePair<string, Socket>? SelectedClient { get; set; }
    public ObservableCollection<FileTransferItem> FileTransferList { get; }

    public string? TcpIp
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "127.0.0.1";

    public int TcpPort
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 5000;

    public string? UdpIp
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "224.0.0.0";

    public int UdpPort
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 9540;

    public int MockCount
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 200000;

    public int MockPageSize
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 5000;

    public DateTime HeartbeatTime
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsRunning
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int ClientCount => ConnectedClients.Count;

    public double TotalProgress
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string TransferSpeed { get; set; } = "0 KB/s";

    public ReactiveCommand<Unit, Unit>? RefreshCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? UpdateCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SelectFilesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? UploadFilesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? DownloadFilesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? CancelTransferCommand { get; private set; }

    #endregion

    #region 文件传输事件处理

    private void OnFileTransferProgress(object? sender, FileTransferProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = FileTransferList.FirstOrDefault(f => f.FileName == e.FileName);
            if (item != null)
            {
                item.Progress = e.Progress;
                item.TransferredBytes = e.TransferredBytes;
                if (e.TransferredBytes >= e.TotalBytes)
                {
                    item.Status = "完成";
                    item.Message = "传输完成";
                }
            }

            UpdateTotalProgress();
        });
    }

    private void UpdateTotalProgress()
    {
        if (FileTransferList.Count == 0)
        {
            TotalProgress = 0;
            return;
        }

        var total = FileTransferList.Sum(f => f.FileSize);
        var transferred = FileTransferList.Sum(f => f.TransferredBytes);
        TotalProgress = total > 0 ? (double)transferred / total * 100 : 0;
    }

    #endregion

    public async Task HandleRunCommandCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            await TcpHelper.StartAsync("TCP服务端", TcpIp, TcpPort);
            UdpHelper.Start("UDP服务端", TcpHelper.SystemId, UdpIp, UdpPort);
            await SendMockDataAsync();
            IsRunning = true;
        }
        else
        {
            await TcpHelper.StopAsync();
            UdpHelper.Stop();
            IsRunning = false;
        }

        await Task.CompletedTask;
    }

    private async Task HandleRefreshCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未运行Tcp服务，无法发送命令");
            return;
        }

        UpdateAllData(true);
    }

    private async Task HandleUpdateCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未运行Tcp服务，无法发送命令");
            return;
        }

        UpdateAllData(false);
    }

    private async Task HandleSelectFilesAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文件",
            AllowMultiple = true
        });

        _selectedFilePaths.Clear();
        foreach (var file in files)
        {
            _selectedFilePaths.Add(file.Path.LocalPath);
        }

        Logger.Info($"已选择 {_selectedFilePaths.Count} 个文件");
    }

    private async Task HandleUploadFilesAsync()
    {
        if (string.IsNullOrEmpty(SelectedClient.Value.Key))
        {
            Logger.Error("请先选择要上传的客户端");
            return;
        }

        if (_selectedFilePaths.Count == 0)
        {
            Logger.Error("请先选择要上传的文件");
            return;
        }

        _uploadCancellation = new CancellationTokenSource();

        foreach (var filePath in _selectedFilePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var item = new FileTransferItem
            {
                FileName = fileName,
                FileSize = new FileInfo(filePath).Length,
                Progress = 0,
                Status = "上传中",
                Message = "等待传输..."
            };
            FileTransferList.Add(item);

            try
            {
                await TcpHelper.StartFileUploadAsync(SelectedClient.Value.Key, filePath, fileName,
                    _uploadCancellation.Token);
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                item.Message = ex.Message;
                Logger.Error($"上传文件失败：{fileName}", ex);
            }
        }
    }

    private async Task HandleDownloadFilesAsync()
    {
        if (string.IsNullOrEmpty(SelectedClient.Value.Key))
        {
            Logger.Error("请先选择要下载的客户端");
            return;
        }

        if (_selectedFilePaths.Count == 0)
        {
            Logger.Error("请先选择要下载的文件");
            return;
        }

        foreach (var filePath in _selectedFilePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var item = new FileTransferItem
            {
                FileName = fileName,
                FileSize = 0,
                Progress = 0,
                Status = "下载中",
                Message = "等待传输..."
            };
            FileTransferList.Add(item);

            try
            {
                await TcpHelper.StartFileDownloadAsync(SelectedClient.Value.Key, filePath);
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                item.Message = ex.Message;
                Logger.Error($"下载文件失败：{fileName}", ex);
            }
        }
    }

    private void HandleCancelTransfer()
    {
        _uploadCancellation?.Cancel();
        Logger.Info("已取消文件传输");
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    #region 处理Socket信息

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
        else if (request.IsCommand<FileTransferStart>())
        {
            var startInfo = request.GetCommand<FileTransferStart>();
            await TcpHelper.HandleFileTransferStartAsync(request.Client!, startInfo.FileName, startInfo.FileSize,
                startInfo.FileHash, startInfo.AlreadyTransferredBytes);
        }
        else if (request.IsCommand<FileBlockData>())
        {
            var blockData = request.GetCommand<FileBlockData>();
            await TcpHelper.HandleFileBlockDataAsync(request.Client!, blockData.BlockIndex, blockData.Offset,
                blockData.BlockSize, blockData.Data);
        }
        else if (request.IsCommand<FileBlockAck>())
        {
            var blockAck = request.GetCommand<FileBlockAck>();
            await TcpHelper.HandleFileBlockAckAsync(request.Client!, blockAck.BlockIndex, blockAck.Success,
                blockAck.Message);
        }
        else
        {
            await ReceiveSocketCommandAsync(request.Client!, request);
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
        await TcpHelper.SendCommandAsync(client, response);

        _ = Log($"响应请求终端类型命令：当前终端为={currentTerminalType.GetDescription()}");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestUdpAddress request)
    {
        _ = Log("收到请求Udp组播地址命令");

        var response = new ResponseUdpAddress()
        {
            TaskId = request.TaskId,
            Ip = UdpIp,
            Port = UdpPort,
        };
        await TcpHelper.SendCommandAsync(client, response);

        _ = Log($"响应请求Udp组播地址命令：{response.Ip}:{response.Port}");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestServiceInfo request)
    {
        _ = Log("收到请求基本信息命令");

        var data = MockUtil.GetBaseInfoAsync().Result!;
        data.TaskId = request.TaskId;
        await TcpHelper.SendCommandAsync(client, data);

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
        await TcpHelper.SendCommandAsync(client, response);

        _ = Log($"响应进程ID列表命令：{response.IDList?.Length}");
    }

    private async Task ReceiveSocketCommandAsync(Socket client, RequestProcessList request)
    {
        if (!MockUtil.IsInitOver)
        {
            _ = Log("模拟数据尚未初始化完成");
            return;
        }

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
                await TcpHelper.SendCommandAsync(client, response);
                await Task.Delay(TimeSpan.FromMilliseconds(10));

                var msg = response.TaskId == default ? "推送" : "响应请求";
                Logger.Info(
                    $"{msg}【{response.PageIndex + 1}/{response.PageCount}】{response.Processes.Count}条({sendCount}/{response.TotalSize})");
            }
        });
    }

    private async Task ReceiveSocketCommandAsync(Socket client, ChangeProcessList changeProcess)
    {
        await TcpHelper.SendCommandAsync(changeProcess);
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
            Logger.Info($"收到正确对象测试：{request.HeadInfo}");
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Success(command.TaskId));
        }
        catch (Exception ex)
        {
            Logger.Error($"收到错误对象测试：{request.HeadInfo}");
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, $"{request.HeadInfo}"));
        }
    }

    private async Task ReceiveRequestStudentListDiffVersionAsync(Socket client, SocketCommand request)
    {
        try
        {
            var currentNetHead = SerializeHelper.GetNetObjectHead<RequestStudentListDiffVersion>();
            var errorMessage =
                $"命令版本异常：命令ID: {request.HeadInfo.ObjectId}，服务端版本{currentNetHead.Version}，客户端版本{request.HeadInfo.ObjectVersion}，服务端不能正常解析";
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, errorMessage));
        }
        catch (Exception ex)
        {
            Logger.Error($"收到错误对象测试：{request.HeadInfo}");
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
                Logger.Info(
                    $"{nameof(RequestStudentListDiffProps)}对象ID({currentNetHead.Id})与版本({currentNetHead.Version})相同，尝试解析：");
                var data = request.GetCommand<RequestStudentListDiffProps>();
                Logger.Info($"按理说这里就不会打印，上面获取代码会抛出异常");
            }
            else
            {
                Logger.Error($"收到错误对象测试：{request.HeadInfo}");
                await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, $"{request.HeadInfo}"));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"收到错误对象测试：{request.HeadInfo}", ex);
            await TcpHelper.SendCommandAsync(client, CommonSocketResponse.Fail(default, $"{request.HeadInfo}：{ex}"));
        }
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
        if (!MockUtil.IsInitOver)
        {
            return;
        }

        if (_isUpdateAll)
        {
            await TcpHelper.SendCommandAsync(new ChangeProcessList());
            Logger.Info("====TCP推送结构变化通知====");
            return;
        }

        await TcpHelper.SendCommandAsync(new UpdateProcessList
        {
            Processes = await MockUtil.MockProcessesAsync(MockCount, 0)
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
        if (!UdpHelper.IsRunning || !MockUtil.IsInitOver) return;

        var sw = Stopwatch.StartNew();

        await MockUtil.MockUpdateDataAsync();

        sw.Stop();

        Logger.Info($"更新模拟实时数据{sw.ElapsedMilliseconds}ms");
    }

    private async void MockSendRealtimeDataAsync(object? sender, ElapsedEventArgs e)
    {
        if (!UdpHelper.IsRunning || !MockUtil.IsInitOver) return;

        var sw = Stopwatch.StartNew();

        MockUtil.MockUpdateRealtimeProcessPageCount(MockCount, SerializeHelper.MaxUdpPacketSize, out var pageSize,
            out var pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            if (!UdpHelper.IsRunning) break;

            var response = MockUtil.MockUpdateRealtimeProcessList(pageSize, pageIndex);
            response.TotalSize = MockCount;
            response.PageSize = pageSize;
            response.PageCount = pageCount;
            response.PageIndex = pageIndex;
            await UdpHelper.SendCommandAsync(response, DateTimeOffset.UtcNow);
        }

        Logger.Info(
            $"推送实时数据{MockCount}条，单包{pageSize}条分{pageCount}包，{sw.ElapsedMilliseconds}ms");
    }

    private async void MockSendGeneralDataAsync(object? sender, ElapsedEventArgs e)
    {
        if (!UdpHelper.IsRunning || !MockUtil.IsInitOver) return;

        var sw = Stopwatch.StartNew();

        MockUtil.MockUpdateGeneralProcessPageCount(MockCount, SerializeHelper.MaxUdpPacketSize, out var pageSize,
            out var pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            if (!UdpHelper.IsRunning) break;

            var response = MockUtil.MockUpdateGeneralProcessList(pageSize, pageIndex);
            response.TotalSize = MockCount;
            response.PageSize = pageSize;
            response.PageCount = pageCount;
            response.PageIndex = pageIndex;

            await UdpHelper.SendCommandAsync(response, DateTimeOffset.UtcNow);
        }

        Logger.Info(
            $"推送一般数据{MockCount}条，单包{pageSize}条分{pageCount}包，{sw.ElapsedMilliseconds}ms");
    }

    #endregion

    private async Task SendMockDataAsync()
    {
        await MockUtil.MockAsync(MockCount);
        _ = Log("数据模拟完成，客户端可以正常请求数据了");
    }

    private void Invoke(Action action)
    {
        Dispatcher.UIThread.Post(action.Invoke);
    }

    private async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action.Invoke);
    }

    private async Task Log(string msg, LogType type = LogType.Info, bool showNotification = true)
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

public class FileTransferItem : ReactiveObject
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public double Progress { get; set; }
    public string Status { get; set; } = "等待";
    public string Message { get; set; } = string.Empty;
    public long TransferredBytes { get; set; }
}