using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件分块确认对象。
/// </summary>
[NetHead(SocketConstants.FileChunkAckObjectId, 2)]
public class FileChunkAck : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把分块确认与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 分块序号。
    /// </summary>
    public long BlockIndex { get; set; }

    /// <summary>
    /// 标记当前分块是否处理成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 远程文件路径。
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 当前已经确认传输完成的字节数。
    /// </summary>
    public long AlreadyTransferredBytes { get; set; }
}
