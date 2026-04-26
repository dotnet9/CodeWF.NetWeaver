namespace SocketDto.Response;

/// <summary>
/// 响应客户端的结束进程请求。
/// </summary>
[NetHead(NetConsts.ResponseTerminateProcessObjectId, 1)]
public class ResponseTerminateProcess : INetObject
{
    /// <summary>
    /// 任务 ID，用于让响应与请求一一对应。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 目标进程 ID。
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// 是否执行成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 服务端返回的说明消息。
    /// </summary>
    public string? Message { get; set; }
    public override string ToString() =>
        $"返回结束进程结果(TaskId={TaskId},Pid={ProcessId},成功={Success},消息={Message})";
}
