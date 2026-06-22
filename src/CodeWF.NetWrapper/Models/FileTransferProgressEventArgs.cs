using System;

namespace CodeWF.NetWrapper.Models;

/// <summary>
///     文件传输进度事件参数。
/// </summary>
public class FileTransferProgressEventArgs : EventArgs
{
    /// <summary>
    ///     初始化文件传输进度事件参数。
    /// </summary>
    public FileTransferProgressEventArgs(int taskId, string fileName, string remoteFilePath, long transferredBytes,
        long totalBytes, double progress, bool isUpload)
    {
        TaskId = taskId;
        FileName = fileName;
        RemoteFilePath = remoteFilePath;
        TransferredBytes = transferredBytes;
        TotalBytes = totalBytes;
        Progress = progress;
        IsUpload = isUpload;
    }

    /// <summary>
    ///     当前传输任务 ID。
    /// </summary>
    public int TaskId { get; }

    /// <summary>
    ///     当前传输文件名。
    /// </summary>
    public string FileName { get; }

    /// <summary>
    ///     协议中的远端文件路径。
    /// </summary>
    public string RemoteFilePath { get; }

    /// <summary>
    ///     已传输字节数。
    /// </summary>
    public long TransferredBytes { get; }

    /// <summary>
    ///     文件总字节数。
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    ///     当前进度百分比。
    /// </summary>
    public double Progress { get; }

    /// <summary>
    ///     是否为上传任务。
    /// </summary>
    public bool IsUpload { get; }
}
