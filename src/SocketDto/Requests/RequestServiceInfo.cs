namespace SocketDto.Requests;

/// <summary>
///     请求基本信息
/// </summary>
[NetHead(NetConsts.RequestServiceInfoObjectId, 1)]
public class RequestServiceInfo : INetObject
{
    /// <summary>
    ///     任务Id
    /// </summary>
    public int TaskId { get; set; }
    public override string ToString() => $"请求服务信息(TaskId={TaskId})";
}
