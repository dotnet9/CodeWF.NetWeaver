using ReactiveUI;
using System;
using System.IO;
using System.Threading;

namespace SocketTest.Client.Features.Transfers.Models;

public class FileTransferItem : ReactiveObject
{
    public Guid Id { get; } = Guid.NewGuid();

    public int TaskId { get; set; }

    public string DisplayName { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public string DestinationPath { get; init; } = string.Empty;

    public FileTransferDirection Direction { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public CancellationTokenSource? CancellationSource { get; set; }

    public bool IsSelected { get; set => this.RaiseAndSetIfChanged(ref field, value); }

    public double Progress
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public long TransferredBytes
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(TransferredText));
        }
    }

    public long TotalBytes
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(TransferredText));
        }
    }

    public FileTransferState State
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(StateText));
            this.RaisePropertyChanged(nameof(ActionText));
            this.RaisePropertyChanged(nameof(HasPrimaryAction));
        }
    } = FileTransferState.Queued;

    public string Detail
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "等待开始";

    public string DirectionText => Direction == FileTransferDirection.Upload ? "上传" : "下载";

    public string StateText => State switch
    {
        FileTransferState.Queued => "排队中",
        FileTransferState.Running => "传输中",
        FileTransferState.Paused => "已暂停",
        FileTransferState.Completed => "已完成",
        FileTransferState.Failed => "失败",
        _ => "未知"
    };

    public string ActionText => State switch
    {
        FileTransferState.Queued => "停止",
        FileTransferState.Running => "停止",
        FileTransferState.Paused => "继续",
        FileTransferState.Failed => "重试",
        _ => string.Empty
    };

    public bool HasPrimaryAction => State != FileTransferState.Completed;

    public string ProgressText => $"{Progress:F1}%";

    public string TransferredText => TotalBytes > 0
        ? $"{FormatBytes(TransferredBytes)} / {FormatBytes(TotalBytes)}"
        : FormatBytes(TransferredBytes);

    public string SourceName => Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public bool CanRemove => State != FileTransferState.Running;

    public void MarkQueued(string? detail = null)
    {
        State = FileTransferState.Queued;
        CompletedAt = null;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            Detail = detail;
        }
    }

    public void MarkRunning(string? detail = null)
    {
        State = FileTransferState.Running;
        StartedAt ??= DateTime.Now;
        CompletedAt = null;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            Detail = detail;
        }
    }

    public void MarkPaused(string? detail = null)
    {
        State = FileTransferState.Paused;
        CompletedAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            Detail = detail;
        }
    }

    public void MarkCompleted(string? detail = null)
    {
        State = FileTransferState.Completed;
        Progress = 100;
        CompletedAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            Detail = detail;
        }
    }

    public void MarkFailed(string? detail = null)
    {
        State = FileTransferState.Failed;
        CompletedAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            Detail = detail;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:F1} MB";
        }

        return $"{bytes / 1024d / 1024d / 1024d:F1} GB";
    }
}
