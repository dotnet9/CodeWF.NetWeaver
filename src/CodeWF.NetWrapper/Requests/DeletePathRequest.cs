using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

[NetHead(SocketConstants.DeletePathRequestObjectId, 1)]
public class DeletePathRequest : INetObject
{
    public int TaskId { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }
}
