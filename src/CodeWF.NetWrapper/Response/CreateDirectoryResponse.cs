using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 创建服务端目录的响应对象。
/// </summary>
[NetHead(SocketConstants.CreateDirectoryResponseObjectId, 1)]
public class CreateDirectoryResponse : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 标记目录创建是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 服务端目录路径。
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;
    public override string ToString() => $"返回创建目录结果(TaskId={TaskId},成功={Success},路径={DirectoryPath},消息={Message})";
}
