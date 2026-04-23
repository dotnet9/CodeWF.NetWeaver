using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 删除文件或目录应答 DTO
/// </summary>
[NetHead(SocketConstants.DeleteFileStartAckObjectId, 1)]
public class DeleteFileStartAck : INetObject
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 文件或目录路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Message { get; set; } = string.Empty;
}