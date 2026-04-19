namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输会话信息，记录一次文件传输的上下文数据
/// </summary>
public class FileTransferSession
{
    /// <summary>文件名</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; set; }
    /// <summary>文件SHA256哈希值，用于校验完整性</summary>
    public string FileHash { get; set; } = string.Empty;
    /// <summary>本地文件路径</summary>
    public string LocalFilePath { get; set; } = string.Empty;
    /// <summary>是否为上传操作（true=上传到服务器，false=从服务器下载）</summary>
    public bool IsUpload { get; set; }
    /// <summary>已传输字节数，用于断点续传</summary>
    public long AlreadyTransferredBytes { get; set; }
}