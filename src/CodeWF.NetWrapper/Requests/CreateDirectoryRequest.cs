using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 在服务端创建目录的请求对象。
/// </summary>
[NetHead(SocketConstants.CreateDirectoryRequestObjectId, 1)]
public class CreateDirectoryRequest : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与当前请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 要创建的目录路径。
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;
    public override string ToString() => $"请求创建目录(TaskId={TaskId},路径={DirectoryPath})";
}
