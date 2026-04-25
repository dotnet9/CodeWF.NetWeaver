using CodeWF.NetWeaver.Base;
using System.Collections.Generic;

namespace CodeWF.NetWrapper.Response;

[NetHead(SocketConstants.DriveListResponseObjectId, 1)]
public class DriveListResponse : INetObject
{
    public int TaskId { get; set; }

    public List<DiskInfo>? Disks { get; set; }
}
