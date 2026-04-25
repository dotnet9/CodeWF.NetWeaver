using CodeWF.NetWrapper.Response;
using System.Collections.Generic;
using System.IO;

namespace CodeWF.NetWrapper.Abstractions;

/// <summary>
/// 服务端文件系统抽象接口。未来接入移动端或沙箱存储时可替换具体实现。
/// </summary>
public interface IManagedFileSystem
{
    IEnumerable<DiskInfo> GetDrives();

    bool DirectoryExists(string path);

    bool FileExists(string path);

    IEnumerable<string> GetFileSystemEntries(string path);

    ManagedFileSystemEntry GetEntry(string path);

    void CreateDirectory(string path);

    void DeleteDirectory(string path, bool recursive);

    void DeleteFile(string path);

    bool PathIsRooted(string path);

    string GetFullPath(string path);

    string Combine(string left, string right);

    string? GetDirectoryName(string path);

    string GetTempPath();

    string ReadAllText(string path);

    void WriteAllText(string path, string content);

    Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share);
}
