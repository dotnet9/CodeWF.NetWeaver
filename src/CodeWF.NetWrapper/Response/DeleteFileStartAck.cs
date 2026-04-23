using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.DeleteFileStartAckObjectId, 1)]
public class DeleteFileStartAck : INetObject
{
    public int TaskId { get; set; }

    public bool Success { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}