using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件块数据 DTO
/// </summary>
[NetHead(SocketConstants.FileBlockDataObjectId, 1)]
public class FileBlockData : INetObject
{
    /// <summary>块索引号</summary>
    public long BlockIndex { get; set; }

    /// <summary>数据偏移量</summary>
    public long Offset { get; set; }

    /// <summary>数据块大小</summary>
    public int BlockSize { get; set; }

    /// <summary>数据内容</summary>
    public byte[] Data { get; set; } = [];
}