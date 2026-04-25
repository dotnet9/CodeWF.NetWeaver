namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 单个磁盘的信息对象。
/// </summary>
public class DiskInfo
{
    /// <summary>
    /// 磁盘名称或盘符。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 磁盘总容量，单位为字节。
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 磁盘可用空间，单位为字节。
    /// </summary>
    public long FreeSpace { get; set; }
}
