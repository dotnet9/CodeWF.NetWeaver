using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 心跳类，用于 Socket 通信中的心跳检测
/// </summary>
[NetHead(SocketConstants.HeartbeatObjectId, 1)]
internal class Heartbeat : INetObject
{
    /// <summary>
    /// 获取或设置任务 ID
    /// </summary>
    public int TaskId { get; set; }
}