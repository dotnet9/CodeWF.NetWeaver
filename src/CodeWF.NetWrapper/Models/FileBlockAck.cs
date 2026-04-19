using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件块传输应答 DTO
/// </summary>
[NetHead(SocketConstants.FileBlockAckObjectId, 1)]
public class FileBlockAck : INetObject
{
    /// <summary>块索引号</summary>
    public long BlockIndex { get; set; }

    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>消息内容</summary>
    public string Message { get; set; } = string.Empty;
}