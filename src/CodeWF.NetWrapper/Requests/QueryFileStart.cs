using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 查询目录请求 DTO
/// </summary>
[NetHead(SocketConstants.QueryFileStartObjectId, 1)]
public class QueryFileStart : INetObject
{
    /// <summary>
    /// 获取或设置任务 ID
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 服务端目录路径
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;
}