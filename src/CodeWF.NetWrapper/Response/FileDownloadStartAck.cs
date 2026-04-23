using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件下载开始应答 DTO
/// </summary>
[NetHead(SocketConstants.FileDownloadStartAckObjectId, 1)]
public class FileDownloadStartAck : INetObject
{
    /// <summary>
    /// 是否接受传输
    /// </summary>
    public bool Accept { get; set; }

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件SHA256哈希值
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 已传输字节数
    /// </summary>
    public long AlreadyTransferredBytes { get; set; }

    /// <summary>
    /// 远程文件路径
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
}