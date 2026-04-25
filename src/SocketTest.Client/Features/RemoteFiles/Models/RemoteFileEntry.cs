using ReactiveUI;
using System;

namespace SocketTest.Client.Features.RemoteFiles.Models;

public class RemoteFileEntry : ReactiveObject
{
    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    public bool IsDrive { get; init; }

    public long Size { get; init; }

    public DateTime LastModifiedTime { get; init; }

    public bool IsSelected { get; set => this.RaiseAndSetIfChanged(ref field, value); }

    public string EntryTypeText => IsDrive ? "磁盘" : IsDirectory ? "文件夹" : "文件";

    public string IconText => IsDrive ? "DRV" : IsDirectory ? "DIR" : "FILE";

    public string SizeText => IsDirectory && !IsDrive ? "--" : FormatBytes(Size);

    public string LastModifiedText => LastModifiedTime == DateTime.MinValue
        ? "--"
        : LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");

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
