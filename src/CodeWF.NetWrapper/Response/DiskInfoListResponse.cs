using CodeWF.NetWeaver.Base;
using System.Collections.Generic;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.DiskInfoListResponseObjectId, 1)]
public class DiskInfoListResponse : INetObject
{
    public int TaskId { get; set; }

    public List<DiskInfo>? Disks { get; set; }
}