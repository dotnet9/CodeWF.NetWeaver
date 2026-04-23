using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.FileDownloadStartAckObjectId, 1)]
public class FileDownloadStartAck : INetObject
{
    public int TaskId { get; set; }

    public bool Accept { get; set; }

    public string Message { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string FileHash { get; set; } = string.Empty;

    public long AlreadyTransferredBytes { get; set; }

    public string RemoteFilePath { get; set; } = string.Empty;
}