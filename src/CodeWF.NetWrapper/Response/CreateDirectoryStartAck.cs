using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 创建目录应答 DTO
/// </summary>
[NetHead(SocketConstants.CreateDirectoryStartAckObjectId, 1)]
public class CreateDirectoryStartAck : INetObject
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 目录路径
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Message { get; set; } = string.Empty;
}