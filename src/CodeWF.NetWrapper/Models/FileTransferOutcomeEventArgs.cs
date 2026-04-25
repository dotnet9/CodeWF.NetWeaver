using System;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输完成、失败或被取消时的结果事件参数。
/// </summary>
public class FileTransferOutcomeEventArgs : EventArgs
{
    /// <summary>
    /// 当前传输任务 ID。
    /// </summary>
    public int TaskId { get; }

    /// <summary>
    /// 当前传输文件名。
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// 协议中的远端文件路径。
    /// </summary>
    public string RemoteFilePath { get; }

    /// <summary>
    /// 是否为上传任务。
    /// </summary>
    public bool IsUpload { get; }

    /// <summary>
    /// 当前结果是否成功。
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// 当前结果是否为用户取消。
    /// </summary>
    public bool IsCancelled { get; }

    /// <summary>
    /// 结果说明消息。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 初始化文件传输结果事件参数。
    /// </summary>
    public FileTransferOutcomeEventArgs(int taskId, string fileName, string remoteFilePath, bool isUpload,
        bool success, bool isCancelled, string message)
    {
        TaskId = taskId;
        FileName = fileName;
        RemoteFilePath = remoteFilePath;
        IsUpload = isUpload;
        Success = success;
        IsCancelled = isCancelled;
        Message = message;
    }
}
