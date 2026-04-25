using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 文件分块数据对象。
/// </summary>
[NetHead(SocketConstants.FileChunkDataObjectId, 2)]
public class FileChunkData : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把分块数据与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 分块序号。
    /// </summary>
    public long BlockIndex { get; set; }

    /// <summary>
    /// 当前分块对应的写入偏移量。
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// 当前分块大小，单位为字节。
    /// </summary>
    public int BlockSize { get; set; }

    /// <summary>
    /// 当前分块的数据内容。
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// 远程文件路径。
    /// </summary>
    public string RemoteFilePath { get; set; } = string.Empty;
}
