using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

[NetHead(SocketConstants.HeartbeatObjectId, 1)]
internal class Heartbeat : INetObject
{
    public int TaskId { get; set; }
}