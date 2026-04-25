using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

[NetHead(SocketConstants.BrowseFileSystemRequestObjectId, 1)]
public class BrowseFileSystemRequest : INetObject
{
    public int TaskId { get; set; }

    public string DirectoryPath { get; set; } = string.Empty;
}
