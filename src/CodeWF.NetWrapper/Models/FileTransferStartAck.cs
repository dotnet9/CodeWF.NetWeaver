using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输开始应答 DTO
/// </summary>
[NetHead(SocketConstants.FileTransferStartAckObjectId, 1)]
public class FileTransferStartAck : INetObject
{
    /// <summary>是否接受传输</summary>
    public bool Accept { get; set; }

    /// <summary>消息内容</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>已传输字节数</summary>
    public long AlreadyTransferredBytes { get; set; }
}