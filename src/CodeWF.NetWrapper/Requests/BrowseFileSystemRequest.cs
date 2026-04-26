using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 浏览服务端文件系统的请求对象。
/// </summary>
[NetHead(SocketConstants.BrowseFileSystemRequestObjectId, 1)]
public class BrowseFileSystemRequest : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与当前请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 要浏览的目录路径。
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;
    public override string ToString() => $"请求浏览文件系统(TaskId={TaskId},路径={DirectoryPath})";
}
