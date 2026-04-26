namespace SocketDto.Requests;

/// <summary>
///     请求目标类型
/// </summary>
[NetHead(NetConsts.RequestTargetTypeObjectId, 1)]
public class RequestTargetType : INetObject
{
    /// <summary>
    ///     任务Id
    /// </summary>
    public int TaskId { get; set; }
    public override string ToString() => $"请求目标类型(TaskId={TaskId})";
}
