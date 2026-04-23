using CodeWF.NetWeaver.Base;
using System.Collections.Generic;
using System.ComponentModel;

namespace CodeWF.NetWrapper.Response;

public enum FileType
{
    [Description("未知")]
    Unknown = 0,

    [Description("文件")]
    File = 1,

    [Description("目录")]
    Directory = 2,

    [Description("快捷方式")]
    Shortcut = 3,

    [Description("符号链接")]
    SymbolicLink = 4,

    [Description("重解析点")]
    ReparsePoint = 5
}

[NetHead(SocketConstants.DirectoryEntryResponseObjectId, 1)]
public class DirectoryEntryResponse : INetObject
{
    public int TaskId { get; set; }

    public int TotalCount { get; set; }

    public int PageSize { get; set; }

    public int PageCount { get; set; }

    public int PageIndex { get; set; }

    public List<FileSystemEntry>? Entries { get; set; }
}

public class FileSystemEntry
{
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    public long LastModifiedTime { get; set; }

    public FileType EntryType { get; set; }
}