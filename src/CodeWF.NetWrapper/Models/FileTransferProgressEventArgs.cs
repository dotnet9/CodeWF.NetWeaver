using System;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输进度事件参数。
/// </summary>
public class FileTransferProgressEventArgs : EventArgs
{
    /// <summary>
    /// 当前传输的文件名。
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// 已经传输完成的字节数。
    /// </summary>
    public long TransferredBytes { get; }

    /// <summary>
    /// 文件总字节数。
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// 当前进度百分比，取值范围通常为 0 到 100。
    /// </summary>
    public double Progress { get; }

    /// <summary>
    /// 标记当前是否为上传操作。
    /// </summary>
    public bool IsUpload { get; }

    /// <summary>
    /// 初始化一个文件传输进度事件参数对象。
    /// </summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="transferredBytes">已传输字节数。</param>
    /// <param name="totalBytes">总字节数。</param>
    /// <param name="progress">当前进度百分比。</param>
    /// <param name="isUpload">是否为上传操作。</param>
    public FileTransferProgressEventArgs(string fileName, long transferredBytes, long totalBytes, double progress,
        bool isUpload)
    {
        FileName = fileName;
        TransferredBytes = transferredBytes;
        TotalBytes = totalBytes;
        Progress = progress;
        IsUpload = isUpload;
    }
}
