using SocketDto.Response;

namespace SocketDto.AutoCommand;

/// <summary>
///     更新进程信息
/// </summary>
[NetHead(NetConsts.UpdateProcessListObjectId, 1)]
public class UpdateProcessList : INetObject
{
    /// <summary>
    ///     进程列表
    /// </summary>
    public List<ProcessItem>? Processes { get; set; }
    public override string ToString() => $"更新进程列表(数量={Processes?.Count ?? 0})";
}
