using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 向服务端发起文件上传的请求对象。
/// </summary>
[NetHead(SocketConstants.FileUploadRequestObjectId, 1)]
public class FileUploadRequest : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与当前请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小，单位为字节。
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件的 SHA-256 哈希值。
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 已经传输完成的字节数，用于断点续传。
    /// </summary>
    public long AlreadyTransferredBytes { get; set; }

    /// <summary>
    /// 服务端保存的目标路径。
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
    public override string ToString() =>
        $"请求上传文件(TaskId={TaskId},文件名={FileName},大小={FileSize},远程路径={RemoteFilePath},已传输={AlreadyTransferredBytes})";
}
