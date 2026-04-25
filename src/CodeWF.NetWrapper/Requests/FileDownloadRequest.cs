using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 从服务端发起文件下载的请求对象。
/// </summary>
[NetHead(SocketConstants.FileDownloadRequestObjectId, 1)]
public class FileDownloadRequest : INetObject
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
    /// 服务端文件路径。
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
}
