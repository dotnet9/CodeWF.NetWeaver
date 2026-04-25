using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件下载请求的响应对象。
/// </summary>
[NetHead(SocketConstants.FileDownloadResponseObjectId, 1)]
public class FileDownloadResponse : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 标记服务端是否接受本次下载。
    /// </summary>
    public bool Accept { get; set; }

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小，单位为字节。
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件的 SHA-256 哈希值。
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 已经确认传输完成的字节数。
    /// </summary>
    public long AlreadyTransferredBytes { get; set; }

    /// <summary>
    /// 服务端文件路径。
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
}
