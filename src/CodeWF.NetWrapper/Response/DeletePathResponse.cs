using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 删除服务端文件或目录的响应对象。
/// </summary>
[NetHead(SocketConstants.DeletePathResponseObjectId, 1)]
public class DeletePathResponse : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 标记删除是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 被删除的目标路径。
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;
    public override string ToString() => $"返回删除路径结果(TaskId={TaskId},成功={Success},路径={FilePath},消息={Message})";
}
