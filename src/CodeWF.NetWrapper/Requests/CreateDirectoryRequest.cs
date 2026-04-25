using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

[NetHead(SocketConstants.CreateDirectoryRequestObjectId, 1)]
public class CreateDirectoryRequest : INetObject
{
    public int TaskId { get; set; }

    public string DirectoryPath { get; set; } = string.Empty;
}
