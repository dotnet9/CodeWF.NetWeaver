namespace CodeWF.NetWrapper;

/// <summary>
/// Socket 通信常量定义类
/// </summary>
public class SocketConstants
{

    /// <summary>
    /// 文件传输开始请求/响应对象 ID
    /// </summary>
    public const ushort FileTransferStartObjectId = 193;

    /// <summary>
    /// 文件传输开始应答对象 ID
    /// </summary>
    public const ushort FileTransferStartAckObjectId = 194;

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
}