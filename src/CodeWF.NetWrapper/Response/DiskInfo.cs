namespace CodeWF.NetWrapper.Response;

public class DiskInfo
{
    public string Name { get; set; } = string.Empty;

    public long TotalSize { get; set; }

    public long FreeSpace { get; set; }
}