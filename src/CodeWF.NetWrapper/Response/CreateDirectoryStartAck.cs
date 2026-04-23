using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.CreateDirectoryStartAckObjectId, 1)]
public class CreateDirectoryStartAck : INetObject
{
    public int TaskId { get; set; }

    public bool Success { get; set; }

    public string DirectoryPath { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}