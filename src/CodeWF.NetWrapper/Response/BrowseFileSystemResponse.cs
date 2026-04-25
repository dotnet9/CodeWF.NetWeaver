using CodeWF.NetWeaver.Base;
using System.Collections.Generic;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.BrowseFileSystemResponseObjectId, 1)]
public class BrowseFileSystemResponse : INetObject
{
    public int TaskId { get; set; }

    public int TotalCount { get; set; }

    public int PageSize { get; set; }

    public int PageCount { get; set; }

    public int PageIndex { get; set; }

    public List<FileSystemEntry>? Entries { get; set; }
}
