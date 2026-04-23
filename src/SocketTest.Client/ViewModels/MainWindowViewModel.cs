using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using CodeWF.NetWrapper.Requests;
using Timer = System.Timers.Timer;
using SocketTest.Client.Dtos;
using SocketTest.Client.Models;
using Notification = Avalonia.Controls.Notifications.Notification;
using CodeWF.NetWrapper.Response;

namespace SocketTest.Client.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public WindowNotificationManager? NotificationManager { get; set; }
    private readonly List<ProcessItemModel> _receivedProcesses = new();

    private int[]? _processIdArray;
    private Dictionary<int, ProcessItemModel>? _processIdAndItems;

    private Timer? _sendDataTimer;
    private int _timestampStartYear;

    public MainWindowViewModel()
    {
        DisplayProcesses = new();
        FileTransferList = new();

        void RegisterCommand()
        {
            RefreshCommand = ReactiveCommand.CreateFromTask(HandleRefreshCommandAsync);
            RefreshAllCommand = ReactiveCommand.CreateFromTask(HandleRefreshAllCommandAsync);
            SendCorrectCommand = ReactiveCommand.CreateFromTask(HandleSendCorrectCommandAsync);
            SendDiffVersionCommand = ReactiveCommand.CreateFromTask(HandleSendDiffVersionCommandAsync);
            SendDiffPropsCommand = ReactiveCommand.CreateFromTask(HandleSendDiffPropsCommandAsync);
            SelectUploadFilesCommand = ReactiveCommand.CreateFromTask(HandleSelectUploadFilesAsync);
            SelectUploadRemoteDirectoryCommand = ReactiveCommand.CreateFromTask(HandleSelectUploadRemoteDirectoryAsync);
            SelectDownloadServerFilesCommand = ReactiveCommand.CreateFromTask(HandleSelectDownloadServerFilesAsync);
            SelectDownloadSaveDirectoryCommand = ReactiveCommand.CreateFromTask(HandleSelectDownloadSaveDirectoryAsync);
            UploadFilesCommand = ReactiveCommand.CreateFromTask(HandleUploadFilesAsync);
            DownloadFilesCommand = ReactiveCommand.CreateFromTask(HandleDownloadFilesAsync);
            CancelTransferCommand = ReactiveCommand.Create(HandleCancelTransfer);
            FileControlCommand = ReactiveCommand.CreateFromTask<FileTransferItem>(HandleFileControlAsync);
            RefreshServerDirectoryCommand = ReactiveCommand.CreateFromTask(HandleRefreshServerDirectoryAsync);
            EnterParentDirectoryCommand = ReactiveCommand.CreateFromTask(HandleEnterParentDirectoryAsync);
            CreateServerDirectoryCommand = ReactiveCommand.CreateFromTask(HandleCreateServerDirectoryAsync);
            DeleteServerItemCommand = ReactiveCommand.CreateFromTask(HandleDeleteServerItemAsync);
            EnterDirectoryCommand = ReactiveCommand.CreateFromTask<string>(HandleEnterDirectoryAsync);
        }

        TcpHelper.FileTransferProgress += OnFileTransferProgress;

        EventBus.Default.Subscribe(this);
        RegisterCommand();

        Logger.Info("连接服务端后获取数据");
    }

    #region 属性

    public Window? Owner { get; set; }
    public RangObservableCollection<ProcessItemModel> DisplayProcesses { get; }

    public TcpSocketClient TcpHelper { get; set; } = new();
    public UdpSocketClient UdpHelper { get; set; } = new();

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

    public string? UdpIp { get; set; }
    public int UdpPort { get; set; }

    public string? BaseInfo { get; set; }

    public bool IsRunning { get; set; }

    public string? SearchKey { get; set; }

    public double TotalProgress
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string TransferSpeed { get; set; } = "0 KB/s";

    public ReactiveCommand<Unit, Unit>? RefreshCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? RefreshAllCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SendCorrectCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SendDiffVersionCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SendDiffPropsCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SelectUploadFilesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SelectUploadRemoteDirectoryCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SelectDownloadServerFilesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? SelectDownloadSaveDirectoryCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? UploadFilesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? DownloadFilesCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? CancelTransferCommand { get; private set; }
    public ReactiveCommand<FileTransferItem, Unit>? FileControlCommand { get; private set; }

    private readonly List<string> _selectedUploadFilePaths = new();
    private string _uploadRemoteDirectory = @"/client/uploads/";
    public string UploadRemoteDirectory
    {
        get => _uploadRemoteDirectory;
        set => this.RaiseAndSetIfChanged(ref _uploadRemoteDirectory, value);
    }

    private string _uploadLocalPaths = string.Empty;
    public string UploadLocalPaths
    {
        get => _uploadLocalPaths;
        set => this.RaiseAndSetIfChanged(ref _uploadLocalPaths, value);
    }

    private string _downloadServerFilePaths = string.Empty;
    public string DownloadServerFilePaths
    {
        get => _downloadServerFilePaths;
        set => this.RaiseAndSetIfChanged(ref _downloadServerFilePaths, value);
    }

    private readonly List<string> _selectedDownloadServerFilePaths = new();
    private string _downloadSaveDirectory = string.Empty;
    public string DownloadSaveDirectory
    {
        get => _downloadSaveDirectory;
        set => this.RaiseAndSetIfChanged(ref _downloadSaveDirectory, value);
    }

    #endregion

    #region 远程文件管理属性

    public ObservableCollection<ServerDirectoryItem> ServerDirectoryItems { get; } = new();

    private ServerDirectoryItem? _selectedServerItem;
    public ServerDirectoryItem? SelectedServerItem
    {
        get => _selectedServerItem;
        set => this.RaiseAndSetIfChanged(ref _selectedServerItem, value);
    }

    private string _currentServerDirectory = @"/";
    public string CurrentServerDirectory
    {
        get => _currentServerDirectory;
        set => this.RaiseAndSetIfChanged(ref _currentServerDirectory, value);
    }

    public ReactiveCommand<Unit, Unit>? RefreshServerDirectoryCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? EnterParentDirectoryCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? CreateServerDirectoryCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? DeleteServerItemCommand { get; private set; }
    public ReactiveCommand<string, Unit>? EnterDirectoryCommand { get; private set; }

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
                    item.CommandText = "完成";
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

    public async Task HandleConnectTcpCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            var (isSuccess, errorMessage) = await TcpHelper.ConnectAsync("TCP客户端", TcpIp, TcpPort);
            if (isSuccess)
            {
                SendHeartbeat();
                await RequestTargetTypeAsync();
                IsRunning = true;
                _ = Log("连接Tcp服务成功");
            }
            else
            {
                _ = Log($"连接Tcp服务失败：{errorMessage}", LogType.Error);
            }
        }
        else
        {
            StopSendHeartbeat();
            TcpHelper.Stop();
            IsRunning = false;
            UdpHelper.Received -= ReceiveUdpCommand;
            UdpHelper.Stop();
            _ = Log("断开Tcp服务");
        }
    }

    private async Task HandleRefreshCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接Tcp服务，无法发送命令");
            return;
        }

        ClearData();
        await TcpHelper.SendCommandAsync(new RequestServiceInfo() { TaskId = NetHelper.GetTaskId() });
    }

    private async Task HandleRefreshAllCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接Tcp服务，无法发送命令");
            return;
        }

        await TcpHelper.SendCommandAsync(new ChangeProcessList());
        Logger.Info("发送刷新所有进程命令");
    }

    private async Task HandleSendCorrectCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接Tcp服务，无法发送命令");
            return;
        }

        await TcpHelper.SendCommandAsync(new RequestStudentListCorrect { TaskId = NetHelper.GetTaskId() });
        Logger.Info("发送模拟相同数据包、相同版本数据包命令");
    }

    private async Task HandleSendDiffVersionCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接Tcp服务，无法发送命令");
            return;
        }

        await TcpHelper.SendCommandAsync(new RequestStudentListDiffVersion { TaskId = NetHelper.GetTaskId() });
        Logger.Info("发送模拟相同数据包、不同版本数据包命令");
    }

    private async Task HandleSendDiffPropsCommandAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接Tcp服务，无法发送命令");
            return;
        }

        await TcpHelper.SendCommandAsync(new RequestStudentListDiffProps { TaskId = "DiffID", Class = "Math" });
        Logger.Info("发送模拟相同数据包、相同版本、不同定义数据包（错误数据包）命令");
    }

    private async Task HandleSelectUploadFilesAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择上传文件(批量)",
            AllowMultiple = true
        });

        _selectedUploadFilePaths.Clear();
        foreach (var file in files)
        {
            _selectedUploadFilePaths.Add(file.Path.LocalPath);
            Logger.Info($"已选择上传文件：{file.Path.LocalPath}");
        }

        UploadLocalPaths = string.Join(",", _selectedUploadFilePaths);
        Logger.Info($"共选择 {_selectedUploadFilePaths.Count} 个上传文件");
    }

    private async Task HandleSelectUploadRemoteDirectoryAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择服务端上传目标目录",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            UploadRemoteDirectory = folder[0].Path.LocalPath;
            Logger.Info($"已选择上传目标目录：{UploadRemoteDirectory}");
        }
    }

    private async Task HandleSelectDownloadServerFilesAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择服务端待下载文件(批量)",
            AllowMultiple = true
        });

        _selectedDownloadServerFilePaths.Clear();
        foreach (var file in files)
        {
            _selectedDownloadServerFilePaths.Add(file.Path.LocalPath);
            Logger.Info($"已选择下载文件：{file.Path.LocalPath}");
        }

        DownloadServerFilePaths = string.Join(",", _selectedDownloadServerFilePaths);
        Logger.Info($"共选择 {_selectedDownloadServerFilePaths.Count} 个下载文件");
    }

    private async Task HandleSelectDownloadSaveDirectoryAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择下载保存目录",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            DownloadSaveDirectory = folder[0].Path.LocalPath;
            Logger.Info($"已选择下载保存目录：{DownloadSaveDirectory}");
        }
    }

    private async Task HandleUploadFilesAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接服务端，无法上传文件");
            return;
        }

        if (string.IsNullOrWhiteSpace(UploadLocalPaths))
        {
            Logger.Error("请输入要上传的文件路径");
            return;
        }

        var separators = new[] { ',', '，' };
        var filePaths = UploadLocalPaths.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (filePaths.Count == 0)
        {
            Logger.Error("请输入要上传的文件路径");
            return;
        }

        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                Logger.Error($"文件不存在：{filePath}");
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            var remoteFilePath = Path.Combine(UploadRemoteDirectory, fileName).Replace("\\", "/");
            var item = new FileTransferItem
            {
                FileName = fileName,
                LocalPath = filePath,
                RemotePath = remoteFilePath,
                TransferType = "上传",
                FileSize = new FileInfo(filePath).Length,
                Progress = 0,
                Status = "上传中",
                LocalFilePath = filePath,
                RemoteFilePath = remoteFilePath,
                IsUpload = true
            };
            FileTransferList.Add(item);
            Logger.Info($"客户端请求上传文件：{filePath} -> {remoteFilePath}");

            var cts = item.GetCancellationTokenSource();
            try
            {
                await TcpHelper.StartFileUploadAsync(filePath, remoteFilePath, cts.Token);
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                item.CommandText = "重试";
                Logger.Error($"上传文件失败：{fileName}", ex);
            }
        }
    }

    private async Task HandleDownloadFilesAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接服务端，无法下载文件");
            return;
        }

        if (string.IsNullOrWhiteSpace(DownloadServerFilePaths))
        {
            Logger.Error("请输入服务端待下载文件路径");
            return;
        }

        if (string.IsNullOrWhiteSpace(DownloadSaveDirectory))
        {
            Logger.Error("请选择本地保存目录");
            return;
        }

        var separators = new[] { ',', '，' };
        var serverFilePaths = DownloadServerFilePaths.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        foreach (var serverFilePath in serverFilePaths)
        {
            var fileName = Path.GetFileName(serverFilePath);
            var localSavePath = Path.Combine(DownloadSaveDirectory, fileName).Replace("\\", "/");
            var item = new FileTransferItem
            {
                FileName = fileName,
                LocalPath = localSavePath,
                RemotePath = serverFilePath,
                TransferType = "下载",
                Progress = 0,
                Status = "下载中",
                LocalFilePath = localSavePath,
                RemoteFilePath = serverFilePath,
                IsUpload = false
            };
            FileTransferList.Add(item);
            Logger.Info($"客户端请求下载文件：{serverFilePath} -> {DownloadSaveDirectory}");

            var cts = item.GetCancellationTokenSource();
            try
            {
                await TcpHelper.StartFileDownloadAsync(serverFilePath, DownloadSaveDirectory);
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                item.CommandText = "重试";
                Logger.Error($"下载文件失败：{fileName}", ex);
            }
        }
    }

    private void HandleCancelTransfer()
    {
        Logger.Info("请使用文件列表中的控制按钮取消单个文件传输");
    }

    private async Task HandleFileControlAsync(FileTransferItem item)
    {
        if (item.Status == "上传中" || item.Status == "下载中")
        {
            item.Cancel();
            Logger.Info($"已停止文件传输：{item.FileName}");
        }
        else if (item.Status == "已停止" || item.Status == "失败" || item.Status == "完成")
        {
            await RestartTransferAsync(item);
        }
    }

    private async Task RestartTransferAsync(FileTransferItem item)
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Error("未连接服务端，无法续传文件");
            return;
        }

        if (item.IsUpload)
        {
            item.Reset();
            var cts = item.GetCancellationTokenSource();
            try
            {
                await TcpHelper.StartFileUploadAsync(item.LocalFilePath!, item.RemoteFilePath!, cts.Token);
                Logger.Info($"续传文件：{item.FileName}");
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                item.CommandText = "重试";
                Logger.Error($"续传失败：{item.FileName}", ex);
            }
        }
        else
        {
            item.Reset();
            var cts = item.GetCancellationTokenSource();
            try
            {
                await TcpHelper.StartFileDownloadAsync(item.RemoteFilePath!, Path.GetDirectoryName(item.LocalFilePath!)!);
                Logger.Info($"续传文件：{item.FileName}");
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                item.CommandText = "重试";
                Logger.Error($"续传失败：{item.FileName}", ex);
            }
        }
    }

    private async Task HandleRefreshServerDirectoryAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            await Log("未连接服务端，无法刷新目录");
            return;
        }

        ServerDirectoryItems.Clear();
        await TcpHelper.SendCommandAsync(new QueryFileStart
        {
            DirectoryPath = CurrentServerDirectory
        });
        await Log($"请求刷新目录：{CurrentServerDirectory}");
    }

    private async Task HandleEnterParentDirectoryAsync()
    {
        if (string.IsNullOrEmpty(CurrentServerDirectory) || CurrentServerDirectory == "/")
        {
            return;
        }

        var parentPath = CurrentServerDirectory.TrimEnd('/');
        var lastSlashIndex = parentPath.LastIndexOf('/');
        CurrentServerDirectory = lastSlashIndex > 0 ? parentPath.Substring(0, lastSlashIndex) + "/" : "/";
        await HandleRefreshServerDirectoryAsync();
    }

    private async Task HandleCreateServerDirectoryAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            await Log("未连接服务端，无法创建目录");
            return;
        }

        var newDirName = "新建目录";
        var newDirPath = CurrentServerDirectory.TrimEnd('/') + "/" + newDirName;
        await TcpHelper.SendCommandAsync(new CreateDirectoryStart
        {
            DirectoryPath = newDirPath
        });
        await Log($"请求创建目录：{newDirPath}");
        await HandleRefreshServerDirectoryAsync();
    }

    private async Task HandleDeleteServerItemAsync()
    {
        if (!TcpHelper.IsRunning)
        {
            await Log("未连接服务端，无法删除");
            return;
        }

        if (SelectedServerItem == null)
        {
            await Log("请先选择要删除的项");
            return;
        }

        await TcpHelper.SendCommandAsync(new DeleteFileStart
        {
            FilePath = SelectedServerItem.FullPath,
            IsDirectory = SelectedServerItem.IsDirectory
        });
        await Log($"请求删除：{SelectedServerItem.FullPath}");
        await HandleRefreshServerDirectoryAsync();
    }

    private async Task HandleEnterDirectoryAsync(string directoryName)
    {
        if (!TcpHelper.IsRunning)
        {
            await Log("未连接服务端，无法进入目录");
            return;
        }

        CurrentServerDirectory = CurrentServerDirectory.TrimEnd('/') + "/" + directoryName;
        await HandleRefreshServerDirectoryAsync();
    }

    private async Task ReceivedSocketMessageAsync(DirectoryEntry entry)
    {
        var item = new ServerDirectoryItem
        {
            Name = entry.Name,
            FullPath = CurrentServerDirectory.TrimEnd('/') + "/" + entry.Name,
            IsDirectory = entry.IsDirectory,
            Size = entry.Size,
            LastModifiedTime = ((uint)entry.LastModifiedTime).FromSpecialUnixTimeSecondsToDateTime(2000)
        };
        ServerDirectoryItems.Add(item);
    }

    private async Task ReceivedSocketMessageAsync(CreateDirectoryStartAck response)
    {
        if (response.Success)
        {
            await Log($"创建目录成功：{response.DirectoryPath}");
        }
        else
        {
            await Log($"创建目录失败：{response.Message}", LogType.Error);
        }
    }

    private async Task ReceivedSocketMessageAsync(DeleteFileStartAck response)
    {
        if (response.Success)
        {
            await Log($"删除成功：{response.FilePath}");
        }
        else
        {
            await Log($"删除失败：{response.Message}", LogType.Error);
        }
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private IEnumerable<ProcessItemModel> FilterData(IEnumerable<ProcessItemModel> processes)
    {
        return string.IsNullOrWhiteSpace(SearchKey)
            ? processes
            : processes.Where(process =>
                !string.IsNullOrWhiteSpace(process.Name) && process.Name.Contains(SearchKey));
    }

    private void ClearData()
    {
        _receivedProcesses.Clear();
        Invoke(DisplayProcesses.Clear);
    }

    private void SendHeartbeat()
    {
        _sendDataTimer = new Timer();
        _sendDataTimer.Interval = 1000;
        _sendDataTimer.Elapsed += MockSendData;
        _sendDataTimer.Start();
        _ = Log("开始发送心跳包");
    }

    private void StopSendHeartbeat()
    {
        if (_sendDataTimer != null)
        {
            _sendDataTimer.Stop();
            _sendDataTimer.Elapsed -= MockSendData;
            _sendDataTimer.Dispose();
            _ = Log("停止发送心跳包");
        }
    }

    private async void MockSendData(object? sender, ElapsedEventArgs e)
    {
        if (!TcpHelper.IsRunning) return;

        await TcpHelper.SendCommandAsync(new Heartbeat());
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

    [EventHandler]
    private async Task ReceivedSocketMessage(SocketCommand message)
    {
        if (message.IsCommand<ResponseTargetType>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<ResponseTargetType>());
        }
        else if (message.IsCommand<ResponseUdpAddress>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<ResponseUdpAddress>());
        }
        else if (message.IsCommand<ResponseServiceInfo>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<ResponseServiceInfo>());
        }
        else if (message.IsCommand<ResponseProcessIDList>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<ResponseProcessIDList>());
        }
        else if (message.IsCommand<ResponseProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseProcessList>());
        }
        else if (message.IsCommand<UpdateProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<UpdateProcessList>());
        }
        else if (message.IsCommand<ChangeProcessList>())
        {
            await HandleRefreshCommandAsync();
        }
        else if (message.IsCommand<Heartbeat>())
        {
            ReceivedSocketMessage(message.GetCommand<Heartbeat>());
        }
        else if (message.IsCommand<CommonSocketResponse>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<CommonSocketResponse>());
        }
        else if (message.IsCommand<DirectoryEntry>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<DirectoryEntry>());
        }
        else if (message.IsCommand<CreateDirectoryStartAck>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<CreateDirectoryStartAck>());
        }
        else if (message.IsCommand<DeleteFileStartAck>())
        {
            await ReceivedSocketMessageAsync(message.GetCommand<DeleteFileStartAck>());
        }
    }

    private void ReceiveUdpCommand(object? sender, SocketCommand command)
    {
        if (command.IsCommand<UpdateRealtimeProcessList>())
        {
            ReceivedSocketMessage(command.GetCommand<UpdateRealtimeProcessList>());
        }
        else if (command.IsCommand<UpdateGeneralProcessList>())
        {
            ReceivedSocketMessage(command.GetCommand<UpdateGeneralProcessList>());
        }
    }

    private async Task ReceivedSocketMessageAsync(ResponseTargetType response)
    {
        var type = (TerminalType)Enum.Parse(typeof(TerminalType), response.Type.ToString());
        if (response.Type == (byte)TerminalType.Server)
        {
            _ = Log($"正确连接{type.GetDescription()}，程序正常运行");

            await TcpHelper.SendCommandAsync(new RequestUdpAddress());
            _ = Log("发送命令获取Udp组播地址");

            await HandleRefreshCommandAsync();
        }
        else
        {
            _ = Log($"目标终端非服务端(type: {type.GetDescription()})，请检查地址是否配置正确（重点检查端口），即将断开连接", LogType.Error);
        }
    }

    private async Task ReceivedSocketMessageAsync(ResponseUdpAddress response)
    {
        UdpIp = response.Ip;
        UdpPort = response.Port;
        _ = Log($"收到Udp组播地址=》{UdpIp}:{UdpPort}");

        await UdpHelper.ConnectAsync("UDP组播", UdpIp, UdpPort, TcpHelper.LocalEndPoint,
            TcpHelper.SystemId);
        UdpHelper.Received += ReceiveUdpCommand;
        _ = Log("尝试订阅Udp组播");
    }

    private async Task ReceivedSocketMessageAsync(ResponseServiceInfo response)
    {
        _timestampStartYear = response.TimestampStartYear;
        var oldBaseInfo = BaseInfo;
        BaseInfo =
            $"更新时间【{response.LastUpdateTime.FromSpecialUnixTimeSecondsToDateTime(response.TimestampStartYear):yyyy:MM:dd HH:mm:ss fff}】：操作系统【{response.OS}】-内存【{response.MemorySize}GB】-处理器【{response.ProcessorCount}个】-硬盘【{response.DiskSize}GB】-带宽【{response.NetworkBandwidth}Mbps】";

        Logger.Info(response.TaskId == default ? "收到服务端推送的基本信息" : "收到请求基本信息响应");
        Logger.Info($"【旧】{oldBaseInfo}");
        Logger.Info($"【新】{BaseInfo}");
        _ = Log(BaseInfo);

        await TcpHelper.SendCommandAsync(new RequestProcessIDList() { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求进程ID列表命令");

        ClearData();
    }

    private async Task ReceivedSocketMessageAsync(ResponseProcessIDList response)
    {
        _processIdArray = response.IDList!;
        _ = Log($"收到进程ID列表，共{_processIdArray.Length}个进程");

        await TcpHelper.SendCommandAsync(new RequestProcessList { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送请求进程详细信息列表命令");
    }

    private void ReceivedSocketMessage(ResponseProcessList response)
    {
        var processes =
            response.Processes?.ConvertAll(process => new ProcessItemModel(process, _timestampStartYear));
        if (!(processes?.Count > 0)) return;

        _receivedProcesses.AddRange(processes);
        var filterData = FilterData(processes);
        Invoke(() => DisplayProcesses.AddRange(filterData));
        if (_receivedProcesses.Count == response.TotalSize)
            _processIdAndItems = _receivedProcesses.ToDictionary(process => process.PID);

        var msg = response.TaskId == default ? "收到推送" : "收到请求响应";
        Logger.Info(
            $"{msg}【{response.PageIndex + 1}/{response.PageCount}】进程{processes.Count}条({_receivedProcesses.Count}/{response.TotalSize})");
    }

    private void ReceivedSocketMessage(UpdateProcessList response)
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

    private void ReceivedSocketMessage(Heartbeat response)
    {
    }

    private async Task ReceivedSocketMessageAsync(CommonSocketResponse response)
    {
        if (response.Status == (byte)TcpResponseStatus.Success)
        {
            await Log(response.Message, LogType.Info);
        }
        else
        {
            await Log(response.Message, LogType.Error);
        }
    }

    #endregion

    #region 接收Udp数据

    private void ReceivedSocketMessage(UpdateRealtimeProcessList response)
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

            Console.WriteLine($"【实时】更新数据{dataCount}条");
        }
        catch (Exception ex)
        {
            Logger.Error($"【实时】更新数据异常：{ex.Message}");
        }
    }

    private void ReceivedSocketMessage(UpdateGeneralProcessList response)
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

            Console.WriteLine($"【实时】更新一般数据{dataCount}条");
        }
        catch (Exception ex)
        {
            Logger.Error($"【实时】更新一般数据异常：{ex.Message}");
        }
    }

    #endregion

    private async Task RequestTargetTypeAsync()
    {
        await TcpHelper.SendCommandAsync(new RequestTargetType() { TaskId = NetHelper.GetTaskId() });
        _ = Log("发送命令查询目标终端类型是否是服务端");
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
    public string LocalPath { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public string TransferType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public double Progress { get; set; }
    public string Status { get; set; } = "等待";
    public long TransferredBytes { get; set; }

    public string? LocalFilePath { get; set; }
    public string? RemoteFilePath { get; set; }
    public bool IsUpload { get; set; }

    private CancellationTokenSource? _cts;
    private string _commandText = "停止";
    public string CommandText
    {
        get => _commandText;
        set => this.RaiseAndSetIfChanged(ref _commandText, value);
    }

    public CancellationTokenSource? GetCancellationTokenSource()
    {
        _cts ??= new CancellationTokenSource();
        return _cts;
    }

    public void Cancel()
    {
        _cts?.Cancel();
        Status = "已停止";
        CommandText = "继续";
        this.RaisePropertyChanged(nameof(Status));
    }

    public void Reset()
    {
        _cts = new CancellationTokenSource();
        Status = "等待";
        CommandText = "停止";
        Progress = 0;
        TransferredBytes = 0;
        this.RaisePropertyChanged(nameof(Status));
        this.RaisePropertyChanged(nameof(Progress));
        this.RaisePropertyChanged(nameof(TransferredBytes));
    }

    public bool IsTransferring => Status == "上传中" || Status == "下载中";
    public bool CanResume => Status == "已停止" || Status == "失败";
}