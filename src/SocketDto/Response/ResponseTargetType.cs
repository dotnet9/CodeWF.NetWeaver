namespace SocketDto.Response;

/// <summary>
///     响应目标终端类型
/// </summary>
[NetHead(NetConsts.ResponseTargetTypeObjectId, 1)]
public class ResponseTargetType : INetObject
{
    /// <summary>
    ///     任务Id
    /// </summary>
    public int TaskId { get; set; }


    /// <summary>
    ///     终端类型，0：Server，1：Client
    /// </summary>
    public byte Type { get; set; }
    public override string ToString() => $"返回目标类型(TaskId={TaskId},类型={Type})";
}
