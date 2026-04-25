using CodeWF.NetWrapper.Abstractions;
using CodeWF.NetWrapper.Response;
using System;
using System.Collections.Generic;
using System.IO;

namespace CodeWF.NetWrapper.Services;

/// <summary>
/// 基于 System.IO 的物理文件系统实现，适用于 Windows、Linux 与 macOS。
/// </summary>
public sealed class PhysicalManagedFileSystem : IManagedFileSystem
{
    public IEnumerable<DiskInfo> GetDrives()
    {
        var drives = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                drives.Add(new DiskInfo
                {
                    Name = drive.Name,
                    TotalSize = drive.TotalSize,
                    FreeSpace = drive.AvailableFreeSpace
                });
            }
            catch
            {
            }
        }

        return drives;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> GetFileSystemEntries(string path) => Directory.GetFileSystemEntries(path);

    public ManagedFileSystemEntry GetEntry(string path)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);

        return new ManagedFileSystemEntry
        {
            Name = info.Name,
            Size = info is FileInfo fileInfo && fileInfo.Exists ? fileInfo.Length : 0,
            LastModifiedTime = info.Exists ? info.LastWriteTime : DateTime.Now,
            Attributes = info.Attributes,
            Extension = info.Extension,
            IsDirectory = info.Attributes.HasFlag(FileAttributes.Directory)
        };
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void DeleteFile(string path) => File.Delete(path);

    public bool PathIsRooted(string path) => Path.IsPathRooted(path);

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string Combine(string left, string right) => Path.Combine(left, right);

    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

    public string GetTempPath() => Path.GetTempPath();

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

    public Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share) =>
        new FileStream(path, mode, access, share);
}
