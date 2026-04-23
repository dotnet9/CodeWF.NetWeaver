using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 删除文件或目录请求 DTO
/// </summary>
[NetHead(SocketConstants.DeleteFileStartObjectId, 1)]
public class DeleteFileStart : INetObject
{
    /// <summary>
    /// 服务端文件或目录路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 是否为目录，true=目录，false=文件
    /// </summary>
    public bool IsDirectory { get; set; }
}