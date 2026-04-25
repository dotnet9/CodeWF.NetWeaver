using Avalonia.Threading;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using ReactiveUI;
using SocketTest.Client.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace SocketTest.Client.ViewModels;

public class FileTransferViewModel : ReactiveObject
{
    private readonly TcpSocketClient _tcpHelper;
    private readonly Timer _updateTimer;
    private DateTime _lastUpdateTime = DateTime.Now;
    private long _lastTotalBytesTransferred;
    private bool _isProcessingQueue;
    private FileTransferItem? _activeTransfer;
    private TaskCompletionSource<FileTransferOutcomeEventArgs>? _activeCompletionSource;
    private double _totalProgress;
    private string _transferSpeed = "0 B/s";
    private string _queueSummary = "暂无传输任务";

    public FileTransferViewModel(TcpSocketClient tcpHelper)
    {
        _tcpHelper = tcpHelper;
        FileTransferList = new ObservableCollection<FileTransferItem>();
        FileTransferList.CollectionChanged += HandleTransferCollectionChanged;

        _tcpHelper.FileTransferProgress += HandleFileTransferProgress;
        _tcpHelper.FileTransferOutcome += HandleFileTransferOutcome;

        PauseSelectedCommand = ReactiveCommand.Create(PauseSelectedTransfers);
        ResumeSelectedCommand = ReactiveCommand.Create(ResumeSelectedTransfers);
        RemoveSelectedCommand = ReactiveCommand.Create(RemoveSelectedTransfers);
        ClearCompletedCommand = ReactiveCommand.Create(ClearCompletedTransfers);
        ToggleTransferCommand = ReactiveCommand.Create<FileTransferItem>(ToggleTransferState);
        RemoveTransferCommand = ReactiveCommand.Create<FileTransferItem>(RemoveTransfer);

        _updateTimer = new Timer(500);
        _updateTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(UpdateDashboard);
        _updateTimer.Start();
    }

    public ObservableCollection<FileTransferItem> FileTransferList { get; }

    public double TotalProgress
    {
        get => _totalProgress;
        private set => this.RaiseAndSetIfChanged(ref _totalProgress, value);
    }

    public string TransferSpeed
    {
        get => _transferSpeed;
        private set => this.RaiseAndSetIfChanged(ref _transferSpeed, value);
    }

    public string QueueSummary
    {
        get => _queueSummary;
        private set => this.RaiseAndSetIfChanged(ref _queueSummary, value);
    }

    public int SelectedCount => FileTransferList.Count(item => item.IsSelected);

    public ReactiveCommand<Unit, Unit> PauseSelectedCommand { get; }

    public ReactiveCommand<Unit, Unit> ResumeSelectedCommand { get; }

    public ReactiveCommand<Unit, Unit> RemoveSelectedCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearCompletedCommand { get; }

    public ReactiveCommand<FileTransferItem, Unit> ToggleTransferCommand { get; }

    public ReactiveCommand<FileTransferItem, Unit> RemoveTransferCommand { get; }

    public void EnqueueUploads(params (string LocalPath, string RemotePath)[] uploads) =>
        EnqueueUploads((System.Collections.Generic.IEnumerable<(string LocalPath, string RemotePath)>)uploads);

    public void EnqueueUploads(System.Collections.Generic.IEnumerable<(string LocalPath, string RemotePath)> uploads)
    {
        foreach (var (localPath, remotePath) in uploads)
        {
            var item = new FileTransferItem
            {
                DisplayName = Path.GetFileName(localPath),
                SourcePath = localPath,
                DestinationPath = remotePath,
                Direction = FileTransferDirection.Upload,
                Detail = "等待上传"
            };
            AttachTransfer(item);
            FileTransferList.Add(item);
        }

        UpdateDashboard();
        TriggerQueueProcessing();
    }

    public void EnqueueDownloads(params (string RemotePath, string LocalPath)[] downloads) =>
        EnqueueDownloads((System.Collections.Generic.IEnumerable<(string RemotePath, string LocalPath)>)downloads);

    public void EnqueueDownloads(System.Collections.Generic.IEnumerable<(string RemotePath, string LocalPath)> downloads)
    {
        foreach (var (remotePath, localPath) in downloads)
        {
            var item = new FileTransferItem
            {
                DisplayName = Path.GetFileName(remotePath.TrimEnd('/', '\\')),
                SourcePath = remotePath,
                DestinationPath = localPath,
                Direction = FileTransferDirection.Download,
                Detail = "等待下载"
            };
            AttachTransfer(item);
            FileTransferList.Add(item);
        }

        UpdateDashboard();
        TriggerQueueProcessing();
    }

    public void ToggleTransfer(FileTransferItem item) => ToggleTransferState(item);

    public void RemoveTransferItem(FileTransferItem item) => RemoveTransfer(item);

    private void PauseSelectedTransfers()
    {
        foreach (var item in FileTransferList.Where(x => x.IsSelected).ToList())
        {
            PauseTransfer(item);
        }
    }

    private void ResumeSelectedTransfers()
    {
        foreach (var item in FileTransferList.Where(x => x.IsSelected).ToList())
        {
            ResumeTransfer(item);
        }
    }

    private void RemoveSelectedTransfers()
    {
        foreach (var item in FileTransferList.Where(x => x.IsSelected).ToList())
        {
            RemoveTransfer(item);
        }
    }

    private void ClearCompletedTransfers()
    {
        foreach (var item in FileTransferList.Where(x => x.State is FileTransferState.Completed or FileTransferState.Failed).ToList())
        {
            if (item.State == FileTransferState.Running)
            {
                continue;
            }

            DetachTransfer(item);
            FileTransferList.Remove(item);
        }

        UpdateDashboard();
    }

    private void ToggleTransferState(FileTransferItem item)
    {
        if (item.State is FileTransferState.Queued or FileTransferState.Running)
        {
            PauseTransfer(item);
            return;
        }

        if (item.State is FileTransferState.Paused or FileTransferState.Failed)
        {
            ResumeTransfer(item);
        }
    }

    private void PauseTransfer(FileTransferItem item)
    {
        if (item.State == FileTransferState.Running)
        {
            item.Detail = "正在停止，保留断点";
            item.CancellationSource?.Cancel();
            return;
        }

        if (item.State == FileTransferState.Queued)
        {
            item.MarkPaused("已暂停，等待继续");
        }

        UpdateDashboard();
    }

    private void ResumeTransfer(FileTransferItem item)
    {
        if (item.State == FileTransferState.Running)
        {
            return;
        }

        item.TaskId = 0;
        item.CancellationSource?.Dispose();
        item.CancellationSource = null;
        item.MarkQueued("准备继续传输");
        UpdateDashboard();
        TriggerQueueProcessing();
    }

    private void RemoveTransfer(FileTransferItem item)
    {
        if (item.State == FileTransferState.Running)
        {
            PauseTransfer(item);
            return;
        }

        DetachTransfer(item);
        FileTransferList.Remove(item);
        UpdateDashboard();
    }

    private void TriggerQueueProcessing()
    {
        if (_isProcessingQueue)
        {
            return;
        }

        _ = ProcessQueueAsync();
    }

    private async Task ProcessQueueAsync()
    {
        if (_isProcessingQueue)
        {
            return;
        }

        _isProcessingQueue = true;
        try
        {
            while (true)
            {
                var nextTransfer = FileTransferList.FirstOrDefault(item => item.State == FileTransferState.Queued);
                if (nextTransfer == null)
                {
                    return;
                }

                _activeTransfer = nextTransfer;
                _activeCompletionSource = new TaskCompletionSource<FileTransferOutcomeEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                nextTransfer.CancellationSource?.Dispose();
                nextTransfer.CancellationSource = new CancellationTokenSource();
                nextTransfer.MarkRunning(nextTransfer.Direction == FileTransferDirection.Upload ? "正在上传" : "正在下载");
                UpdateDashboard();

                try
                {
                    if (nextTransfer.Direction == FileTransferDirection.Upload)
                    {
                        await _tcpHelper.UploadFileAsync(nextTransfer.SourcePath, nextTransfer.DestinationPath,
                            nextTransfer.CancellationSource.Token);
                    }
                    else
                    {
                        var localDirectory = Path.GetDirectoryName(nextTransfer.DestinationPath);
                        if (string.IsNullOrWhiteSpace(localDirectory))
                        {
                            throw new InvalidOperationException("下载目标目录无效。");
                        }

                        Directory.CreateDirectory(localDirectory);
                        await _tcpHelper.DownloadFileAsync(nextTransfer.SourcePath, localDirectory,
                            nextTransfer.CancellationSource.Token);
                    }

                    var outcome = await _activeCompletionSource.Task.WaitAsync(nextTransfer.CancellationSource.Token);
                    ApplyTransferOutcome(nextTransfer, outcome);
                }
                catch (OperationCanceledException)
                {
                    nextTransfer.MarkPaused("已停止，可继续传输");
                }
                catch (Exception ex)
                {
                    Logger.Error($"文件传输任务异常：{nextTransfer.DisplayName}", ex);
                    nextTransfer.MarkFailed(ex.Message);
                }
                finally
                {
                    _activeTransfer = null;
                    _activeCompletionSource = null;
                    nextTransfer.CancellationSource?.Dispose();
                    nextTransfer.CancellationSource = null;
                    UpdateDashboard();
                }
            }
        }
        finally
        {
            _isProcessingQueue = false;
        }
    }

    private void HandleFileTransferProgress(object? sender, FileTransferProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = FindTransferItem(e);
            if (item == null)
            {
                return;
            }

            if (item.TaskId == 0)
            {
                item.TaskId = e.TaskId;
            }

            item.TotalBytes = e.TotalBytes;
            item.TransferredBytes = e.TransferredBytes;
            item.Progress = e.Progress;
            if (item.State != FileTransferState.Running)
            {
                item.MarkRunning(e.IsUpload ? "正在上传" : "正在下载");
            }

            UpdateDashboard();
        });
    }

    private void HandleFileTransferOutcome(object? sender, FileTransferOutcomeEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = FindTransferItem(e);
            if (item == null)
            {
                return;
            }

            if (item.TaskId == 0)
            {
                item.TaskId = e.TaskId;
            }

            ApplyTransferOutcome(item, e);
            if (ReferenceEquals(_activeTransfer, item))
            {
                _activeCompletionSource?.TrySetResult(e);
            }

            UpdateDashboard();
        });
    }

    private void ApplyTransferOutcome(FileTransferItem item, FileTransferOutcomeEventArgs outcome)
    {
        if (outcome.IsCancelled)
        {
            item.MarkPaused(string.IsNullOrWhiteSpace(outcome.Message) ? "已停止，可继续传输" : outcome.Message);
            return;
        }

        if (outcome.Success)
        {
            item.MarkCompleted(string.IsNullOrWhiteSpace(outcome.Message) ? "传输完成" : outcome.Message);
            return;
        }

        item.MarkFailed(string.IsNullOrWhiteSpace(outcome.Message) ? "传输失败" : outcome.Message);
    }

    private FileTransferItem? FindTransferItem(FileTransferProgressEventArgs args)
    {
        if (_activeTransfer != null && IsMatch(_activeTransfer, args.RemoteFilePath, args.IsUpload))
        {
            return _activeTransfer;
        }

        return FileTransferList.FirstOrDefault(item => IsMatch(item, args.RemoteFilePath, args.IsUpload));
    }

    private FileTransferItem? FindTransferItem(FileTransferOutcomeEventArgs args)
    {
        if (_activeTransfer != null && IsMatch(_activeTransfer, args.RemoteFilePath, args.IsUpload))
        {
            return _activeTransfer;
        }

        return FileTransferList.FirstOrDefault(item => IsMatch(item, args.RemoteFilePath, args.IsUpload));
    }

    private static bool IsMatch(FileTransferItem item, string remoteFilePath, bool isUpload)
    {
        var itemRemotePath = item.Direction == FileTransferDirection.Upload
            ? item.DestinationPath
            : item.SourcePath;

        return item.Direction == (isUpload ? FileTransferDirection.Upload : FileTransferDirection.Download) &&
               string.Equals(NormalizePath(itemRemotePath), NormalizePath(remoteFilePath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim();

    private void HandleTransferCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<FileTransferItem>())
            {
                AttachTransfer(item);
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<FileTransferItem>())
            {
                DetachTransfer(item);
            }
        }

        this.RaisePropertyChanged(nameof(SelectedCount));
    }

    private void AttachTransfer(FileTransferItem item)
    {
        item.PropertyChanged -= HandleTransferPropertyChanged;
        item.PropertyChanged += HandleTransferPropertyChanged;
    }

    private void DetachTransfer(FileTransferItem item)
    {
        item.PropertyChanged -= HandleTransferPropertyChanged;
    }

    private void HandleTransferPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileTransferItem.IsSelected) or nameof(FileTransferItem.Progress) or
            nameof(FileTransferItem.State) or nameof(FileTransferItem.TransferredBytes))
        {
            UpdateDashboard();
            this.RaisePropertyChanged(nameof(SelectedCount));
        }
    }

    private void UpdateDashboard()
    {
        if (FileTransferList.Count == 0)
        {
            TotalProgress = 0;
            TransferSpeed = "0 B/s";
            QueueSummary = "暂无传输任务";
            return;
        }

        TotalProgress = FileTransferList.Average(item => item.Progress);

        var totalBytesTransferred = FileTransferList.Sum(item => item.TransferredBytes);
        var elapsedSeconds = (DateTime.Now - _lastUpdateTime).TotalSeconds;
        if (elapsedSeconds > 0)
        {
            var deltaBytes = totalBytesTransferred - _lastTotalBytesTransferred;
            TransferSpeed = FormatBytesPerSecond((long)(deltaBytes / elapsedSeconds));
        }

        _lastTotalBytesTransferred = totalBytesTransferred;
        _lastUpdateTime = DateTime.Now;

        var running = FileTransferList.Count(item => item.State == FileTransferState.Running);
        var queued = FileTransferList.Count(item => item.State == FileTransferState.Queued);
        var paused = FileTransferList.Count(item => item.State == FileTransferState.Paused);
        var completed = FileTransferList.Count(item => item.State == FileTransferState.Completed);
        var failed = FileTransferList.Count(item => item.State == FileTransferState.Failed);

        QueueSummary = $"运行中 {running}，排队 {queued}，暂停 {paused}，完成 {completed}，失败 {failed}";
    }

    private static string FormatBytesPerSecond(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B/s";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:F1} KB/s";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:F1} MB/s";
        }

        return $"{bytes / 1024d / 1024d / 1024d:F1} GB/s";
    }
}
