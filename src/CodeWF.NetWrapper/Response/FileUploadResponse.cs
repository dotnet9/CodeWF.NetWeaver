using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件上传请求的响应对象。
/// </summary>
[NetHead(SocketConstants.FileUploadResponseObjectId, 1)]
public class FileUploadResponse : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 标记服务端是否接受本次上传。
    /// </summary>
    public bool Accept { get; set; }

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 已经确认传输完成的字节数。
    /// </summary>
    public long AlreadyTransferredBytes { get; set; }

    /// <summary>
    /// 服务端文件路径。
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
}
