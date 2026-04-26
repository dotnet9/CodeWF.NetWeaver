namespace SocketDto.Requests;

/// <summary>
/// 请求服务端结束指定进程树。
/// </summary>
[NetHead(NetConsts.RequestTerminateProcessObjectId, 1)]
public class RequestTerminateProcess : INetObject
{
    /// <summary>
    /// 任务 ID，用于让响应与请求一一对应。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 需要结束的目标进程 ID。
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// 是否同时结束该进程的所有子进程。
    /// </summary>
    public bool KillEntireProcessTree { get; set; } = true;
    public override string ToString() =>
        $"请求结束进程(TaskId={TaskId},Pid={ProcessId},结束进程树={KillEntireProcessTree})";
}
