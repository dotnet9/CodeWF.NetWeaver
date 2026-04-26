namespace SocketDto.Requests;

/// <summary>
///     请求进程ID列表信息
/// </summary>
[NetHead(NetConsts.RequestProcessIDListObjectId, 1)]
public class RequestProcessIDList : INetObject
{
    /// <summary>
    ///     任务Id
    /// </summary>
    public int TaskId { get; set; }
    public override string ToString() => $"请求进程ID列表(TaskId={TaskId})";
}
