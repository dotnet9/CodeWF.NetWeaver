using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件下载开始请求 DTO
/// </summary>
[NetHead(SocketConstants.FileDownloadStartObjectId, 1)]
public class FileDownloadStart : INetObject
{
    /// <summary>
    /// 文件名（不含路径）
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件SHA256哈希值
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 已传输字节数（断点续传用）
    /// </summary>
    public long AlreadyTransferredBytes { get; set; }

    /// <summary>
    /// 服务端待下载文件路径（含文件名）
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
}