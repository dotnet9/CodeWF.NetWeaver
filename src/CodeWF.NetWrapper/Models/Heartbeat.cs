using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 心跳通信对象，用于检测连接是否仍然存活。
/// </summary>
[NetHead(SocketConstants.HeartbeatObjectId, 1)]
public class Heartbeat : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }
}
