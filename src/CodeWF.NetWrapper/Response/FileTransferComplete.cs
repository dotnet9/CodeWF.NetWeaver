using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件传输完成 DTO
/// </summary>
[NetHead(SocketConstants.FileTransferCompleteObjectId, 1)]
public class FileTransferComplete : INetObject
{
    /// <summary>文件哈希值</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>消息内容</summary>
    public string Message { get; set; } = string.Empty;
}