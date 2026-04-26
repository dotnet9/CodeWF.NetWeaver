namespace SocketDto;

/// <summary>
///     请求进程信息
/// </summary>
[NetHead(NetConsts.RequestProcessListObjectId, 1)]
public class RequestProcessList : INetObject
{
    /// <summary>
    ///     任务Id
    /// </summary>
    public int TaskId { get; set; }
    public override string ToString() => $"请求进程列表(TaskId={TaskId})";
}
