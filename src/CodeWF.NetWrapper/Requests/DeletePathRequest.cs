using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 删除服务端文件或目录的请求对象。
/// </summary>
[NetHead(SocketConstants.DeletePathRequestObjectId, 1)]
public class DeletePathRequest : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与当前请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 要删除的目标路径。
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 标记当前路径是否为目录。
    /// </summary>
    public bool IsDirectory { get; set; }
    public override string ToString() => $"请求删除路径(TaskId={TaskId},路径={FilePath},目录={IsDirectory})";
}
