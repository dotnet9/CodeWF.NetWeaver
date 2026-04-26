using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件传输完成对象。
/// </summary>
[NetHead(SocketConstants.FileTransferCompleteObjectId, 1)]
public class FileTransferComplete : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把完成消息与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 文件的 SHA-256 哈希值。
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 标记本次传输是否成功完成。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;
    public override string ToString() => $"文件传输完成(TaskId={TaskId},成功={Success},消息={Message})";
}
