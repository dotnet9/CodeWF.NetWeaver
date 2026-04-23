namespace CodeWF.NetWrapper;

/// <summary>
/// Socket 通信常量定义类
/// </summary>
public class SocketConstants
{

    /// <summary>
    /// 文件上传开始请求对象 ID
    /// </summary>
    public const ushort FileUploadStartObjectId = 193;

    /// <summary>
    /// 文件上传开始应答对象 ID
    /// </summary>
    public const ushort FileUploadStartAckObjectId = 194;

    /// <summary>
    /// 文件块数据对象 ID
    /// </summary>
    public const ushort FileBlockDataObjectId = 195;

    /// <summary>
    /// 文件块传输应答对象 ID
    /// </summary>
    public const ushort FileBlockAckObjectId = 196;

    /// <summary>
    /// 文件传输完成对象 ID
    /// </summary>
    public const ushort FileTransferCompleteObjectId = 197;

    /// <summary>
    /// 通用 Socket 响应对象 ID
    /// </summary>
    public const ushort CommonSocketResponseObjectId = 198;
    /// <summary>
    /// 心跳对象 ID
    /// </summary>
    public const ushort HeartbeatObjectId = 199;

    /// <summary>
    /// 查询目录请求对象 ID
    /// </summary>
    public const ushort QueryFileStartObjectId = 200;

    /// <summary>
    /// 目录条目对象 ID
    /// </summary>
    public const ushort DirectoryEntryObjectId = 201;

    /// <summary>
    /// 创建目录请求对象 ID
    /// </summary>
    public const ushort CreateDirectoryStartObjectId = 202;

    /// <summary>
    /// 创建目录应答对象 ID
    /// </summary>
    public const ushort CreateDirectoryStartAckObjectId = 203;

    /// <summary>
    /// 删除文件请求对象 ID
    /// </summary>
    public const ushort DeleteFileStartObjectId = 204;

    /// <summary>
    /// 删除文件应答对象 ID
    /// </summary>
    public const ushort DeleteFileStartAckObjectId = 205;

    /// <summary>
    /// 文件传输拒绝对象 ID
    /// </summary>
    public const ushort FileTransferRejectObjectId = 206;

    /// <summary>
    /// 文件下载开始请求对象 ID
    /// </summary>
    public const ushort FileDownloadStartObjectId = 207;

    /// <summary>
    /// 文件下载开始应答对象 ID
    /// </summary>
    public const ushort FileDownloadStartAckObjectId = 208;
}