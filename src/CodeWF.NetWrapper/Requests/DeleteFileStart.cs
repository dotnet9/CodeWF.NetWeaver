using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

[NetHead(SocketConstants.DeleteFileStartObjectId, 1)]
public class DeleteFileStart : INetObject
{
    public int TaskId { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }
}