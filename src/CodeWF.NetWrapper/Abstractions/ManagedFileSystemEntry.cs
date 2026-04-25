using System;
using System.IO;

namespace CodeWF.NetWrapper.Abstractions;

/// <summary>
/// 文件系统条目抽象对象。
/// </summary>
public sealed class ManagedFileSystemEntry
{
    public string Name { get; init; } = string.Empty;

    public long Size { get; init; }

    public DateTime LastModifiedTime { get; init; }

    public FileAttributes Attributes { get; init; }

    public string? Extension { get; init; }

    public bool IsDirectory { get; init; }
}
