using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CodeWF.Log.Core;
using ReactiveUI;
using SocketTest.Client.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using Timer = System.Timers.Timer;

namespace SocketTest.Client.ViewModels;

public class FileTransferViewModel : ReactiveObject
{
    private Timer? _updateTimer;
    private DateTime _lastUpdateTime = DateTime.Now;
    private long _lastTotalBytesTransferred;

    public FileTransferViewModel()
    {
        FileTransferList = new();
        RegisterCommand();
        StartUpdateTimer();
        Logger.Info("文件传输初始化完成");
    }

    public ObservableCollection<FileTransferItem> FileTransferList { get; }

    public string? UploadLocalPaths { get; set; } = string.Empty;
    public string? UploadRemoteDirectory { get; set; } = "/";
    public string? DownloadServerFilePaths { get; set; } = string.Empty;
    public string? DownloadSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private double _totalProgress;
    public double TotalProgress
    {
        get => _totalProgress;
        set => this.RaiseAndSetIfChanged(ref _totalProgress, value);
    }

    private string _transferSpeed = "0 B/s";
    public string TransferSpeed
    {
        get => _transferSpeed;
        set => this.RaiseAndSetIfChanged(ref _transferSpeed, value);
    }

    public ReactiveCommand<Unit, Unit> SelectUploadFilesCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SelectUploadRemoteDirectoryCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SelectDownloadServerFilesCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SelectDownloadSaveDirectoryCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> UploadFilesCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> DownloadFilesCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> CancelTransferCommand { get; private set; } = null!;
    public ReactiveCommand<FileTransferItem, Unit> FileControlCommand { get; private set; } = null!;

    private void RegisterCommand()
    {
        SelectUploadFilesCommand = ReactiveCommand.Create(HandleSelectUploadFiles);
        SelectUploadRemoteDirectoryCommand = ReactiveCommand.Create(HandleSelectUploadRemoteDirectory);
        SelectDownloadServerFilesCommand = ReactiveCommand.Create(HandleSelectDownloadServerFiles);
        SelectDownloadSaveDirectoryCommand = ReactiveCommand.Create(HandleSelectDownloadSaveDirectory);
        UploadFilesCommand = ReactiveCommand.Create(HandleUploadFiles);
        DownloadFilesCommand = ReactiveCommand.Create(HandleDownloadFiles);
        CancelTransferCommand = ReactiveCommand.Create(HandleCancelTransfer);
        FileControlCommand = ReactiveCommand.Create<FileTransferItem>(HandleFileControl);
    }

    private void StartUpdateTimer()
    {
        _updateTimer = new Timer(500);
        _updateTimer.Elapsed += OnUpdateTimerElapsed;
        _updateTimer.Start();
    }

    private void OnUpdateTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CalculateTotalProgress();
            CalculateTransferSpeed();
        });
    }

    private void CalculateTotalProgress()
    {
        if (FileTransferList.Count == 0)
        {
            TotalProgress = 0;
            return;
        }

        double totalProgress = 0;
        foreach (var item in FileTransferList)
        {
            totalProgress += item.Progress;
        }
        TotalProgress = totalProgress / FileTransferList.Count;
    }

    private void CalculateTransferSpeed()
    {
        var totalBytesTransferred = FileTransferList.Sum(x => x.TransferredBytes);
        var elapsed = (DateTime.Now - _lastUpdateTime).TotalSeconds;
        if (elapsed > 0)
        {
            var bytesPerSecond = (long)((totalBytesTransferred - _lastTotalBytesTransferred) / elapsed);
            TransferSpeed = FormatBytesPerSecond(bytesPerSecond);
        }
        _lastTotalBytesTransferred = totalBytesTransferred;
        _lastUpdateTime = DateTime.Now;
    }

    private static string FormatBytesPerSecond(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B/s";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB/s";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB/s";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB/s";
    }

    private async void HandleSelectUploadFiles()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择上传文件",
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            UploadLocalPaths = string.Join(",", files.Select(f => f.Path.LocalPath));
        }
    }

    private void HandleSelectUploadRemoteDirectory()
    {
    }

    private void HandleSelectDownloadServerFiles()
    {
    }

    private async void HandleSelectDownloadSaveDirectory()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择保存目录"
        });

        if (folders.Count > 0)
        {
            DownloadSaveDirectory = folders[0].Path.LocalPath;
        }
    }

    private void HandleUploadFiles()
    {
        if (string.IsNullOrWhiteSpace(UploadLocalPaths))
        {
            Logger.Warn("请选择要上传的文件");
            return;
        }

        var filePaths = UploadLocalPaths.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                Logger.Warn($"文件不存在：{filePath}");
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            var remotePath = Path.Combine(UploadRemoteDirectory ?? "/", fileName).Replace("\\", "/");

            var item = new FileTransferItem
            {
                LocalPath = filePath,
                RemotePath = remotePath,
                TransferType = "上传",
                Status = "等待",
                CommandText = "停止"
            };
            FileTransferList.Add(item);
        }
    }

    private void HandleDownloadFiles()
    {
        if (string.IsNullOrWhiteSpace(DownloadServerFilePaths))
        {
            Logger.Warn("请选择要下载的文件");
            return;
        }

        if (string.IsNullOrWhiteSpace(DownloadSaveDirectory))
        {
            Logger.Warn("请选择保存目录");
            return;
        }

        var filePaths = DownloadServerFilePaths.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var localPath = Path.Combine(DownloadSaveDirectory, fileName);

            var item = new FileTransferItem
            {
                LocalPath = localPath,
                RemotePath = filePath,
                TransferType = "下载",
                Status = "等待",
                CommandText = "停止"
            };
            FileTransferList.Add(item);
        }
    }

    private void HandleCancelTransfer()
    {
    }

    private void HandleFileControl(FileTransferItem item)
    {
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}