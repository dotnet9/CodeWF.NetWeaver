using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件传输拒绝 DTO
/// </summary>
[NetHead(SocketConstants.FileTransferRejectObjectId, 1)]
public class FileTransferReject : INetObject
{
    /// <summary>
    /// 错误码
    /// </summary>
    public int ErrorCode { get; set; }

    /// <summary>
    /// 错误描述
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 远程文件路径
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}