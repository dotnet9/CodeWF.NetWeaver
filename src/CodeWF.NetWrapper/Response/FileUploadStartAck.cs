using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.FileUploadStartAckObjectId, 1)]
public class FileUploadStartAck : INetObject
{
    public int TaskId { get; set; }

    public bool Accept { get; set; }

    public string Message { get; set; } = string.Empty;

    public long AlreadyTransferredBytes { get; set; }

    public string RemoteFilePath { get; set; } = string.Empty;
}