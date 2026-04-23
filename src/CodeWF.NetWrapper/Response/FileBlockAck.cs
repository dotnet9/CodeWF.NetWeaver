using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件块传输应答 DTO
/// </summary>
[NetHead(SocketConstants.FileBlockAckObjectId, 2)]
public class FileBlockAck : INetObject
{
    /// <summary>块索引号</summary>
    public long BlockIndex { get; set; }

    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>消息内容</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>远程文件路径</summary>
    public string RemoteFilePath { get; set; } = string.Empty;

    /// <summary>已传输字节数</summary>
    public long AlreadyTransferredBytes { get; set; }
}