using ReactiveUI;
using System;

namespace SocketTest.Client.Models;

public class ServerDirectoryItem : ReactiveObject
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModifiedTime { get; set; }
    public bool IsSelected { get; set; }
}