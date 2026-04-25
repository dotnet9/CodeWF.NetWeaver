namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输会话信息，用于记录一次传输过程中的上下文数据。
/// </summary>
public class FileTransferSession
{
    /// <summary>
    /// 当前传输的文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小，单位为字节。
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件的 SHA-256 哈希值，用于校验完整性。
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 本地文件路径。
    /// </summary>
    public string LocalFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 标记当前是否为上传操作。
    /// `true` 表示上传到服务端，`false` 表示从服务端下载。
    /// </summary>
    public bool IsUpload { get; set; }

    /// <summary>
    /// 已经传输完成的字节数，用于断点续传。
    /// </summary>
    public long AlreadyTransferredBytes { get; set; }
}
