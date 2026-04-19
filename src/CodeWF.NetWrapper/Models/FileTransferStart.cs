using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输开始请求/响应 DTO
/// </summary>
[NetHead(SocketConstants.FileTransferStartObjectId, 1)]
public class FileTransferStart : INetObject
{
    /// <summary>文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; set; }

    /// <summary>文件SHA256哈希值</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>已传输字节数（断点续传用）</summary>
    public long AlreadyTransferredBytes { get; set; }

    /// <summary>是否为上传操作（true=上传，false=下载）</summary>
    public bool IsUpload { get; set; }
}