using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

[NetHead(SocketConstants.FileUploadStartObjectId, 1)]
public class FileUploadStart : INetObject
{
    public int TaskId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string FileHash { get; set; } = string.Empty;

    public long AlreadyTransferredBytes { get; set; }

    public string RemoteFilePath { get; set; } = string.Empty;
}