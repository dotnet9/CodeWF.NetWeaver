using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 目录条目信息 DTO
/// </summary>
[NetHead(SocketConstants.DirectoryEntryObjectId, 1)]
public class DirectoryEntry : INetObject
{
    /// <summary>
    /// 文件或目录名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节），目录为0
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 文件或目录修改时间
    /// </summary>
    public long LastModifiedTime { get; set; }

    /// <summary>
    /// 是否为目录，true=目录，false=文件
    /// </summary>
    public bool IsDirectory { get; set; }
}