using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Models;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.FileTransferRejectObjectId, 1)]
public class FileTransferReject : INetObject
{
    public int TaskId { get; set; }

    public FileTransferErrorCode ErrorCode { get; set; }

    public string Message { get; set; } = string.Empty;

    public string RemoteFilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
}