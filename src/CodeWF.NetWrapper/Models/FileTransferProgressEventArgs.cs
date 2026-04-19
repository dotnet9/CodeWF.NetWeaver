using System;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输进度事件参数
/// </summary>
public class FileTransferProgressEventArgs : EventArgs
{
    /// <summary>文件名</summary>
    public string FileName { get; }

    /// <summary>已传输字节数</summary>
    public long TransferredBytes { get; }

    /// <summary>总字节数</summary>
    public long TotalBytes { get; }

    /// <summary>进度百分比（0-100）</summary>
    public double Progress { get; }

    /// <summary>是否为上传操作</summary>
    public bool IsUpload { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="transferredBytes">已传输字节数</param>
    /// <param name="totalBytes">总字节数</param>
    /// <param name="progress">进度百分比</param>
    /// <param name="isUpload">是否为上传操作</param>
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