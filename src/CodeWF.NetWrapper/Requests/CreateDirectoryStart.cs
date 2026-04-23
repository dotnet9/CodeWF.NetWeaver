using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

[NetHead(SocketConstants.CreateDirectoryStartObjectId, 1)]
public class CreateDirectoryStart : INetObject
{
    public int TaskId { get; set; }

    public string DirectoryPath { get; set; } = string.Empty;
}