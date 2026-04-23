using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 文件上传开始请求 DTO
/// </summary>
[NetHead(SocketConstants.FileUploadStartObjectId, 1)]
public class FileUploadStart : INetObject
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
    /// 服务端保存路径（含文件名）
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
}