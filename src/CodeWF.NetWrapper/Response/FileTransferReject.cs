using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Models;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件传输被拒绝时的响应对象。
/// </summary>
[NetHead(SocketConstants.FileTransferRejectObjectId, 1)]
public class FileTransferReject : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 传输失败或拒绝的错误码。
    /// </summary>
    public FileTransferErrorCode ErrorCode { get; set; }

    /// <summary>
    /// 具体错误消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 远程文件路径。
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    public override string ToString() =>
        $"文件传输拒绝(TaskId={TaskId},错误码={ErrorCode},路径={RemoteFilePath},消息={Message})";
}
