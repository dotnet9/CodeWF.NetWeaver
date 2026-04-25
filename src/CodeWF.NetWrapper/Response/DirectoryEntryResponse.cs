using System;
using CodeWF.NetWeaver.Base;
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

/// <summary>
/// 兼容旧版“浏览文件系统响应”名称，请改用 <see cref="BrowseFileSystemResponse"/>。
/// </summary>
[Obsolete("Use BrowseFileSystemResponse instead.")]
[NetHead(SocketConstants.BrowseFileSystemResponseObjectId, 1)]
public class DirectoryEntryResponse : BrowseFileSystemResponse
{
}

public class FileSystemEntry
{
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    public long LastModifiedTime { get; set; }

    public FileType EntryType { get; set; }
}
