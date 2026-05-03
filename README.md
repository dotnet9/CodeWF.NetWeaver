# CodeWF.NetWeaver

[![NuGet](https://img.shields.io/nuget/v/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.NetWeaver)](LICENSE)

`CodeWF.NetWeaver` is the serialization core for network packets.

`CodeWF.NetWrapper` builds on top of it and provides TCP/UDP helper classes, command dispatching, and file transfer / file management capabilities.

Chinese version: [README.zh-CN.md](README.zh-CN.md)

## Projects

| Project | Description |
| --- | --- |
| `CodeWF.NetWeaver` | Core packet serialization and deserialization library. |
| `CodeWF.NetWrapper` | TCP/UDP socket helper library built on top of `CodeWF.NetWeaver`. |
| `SocketTest.Client` | Demo client for wrapper features. |
| `SocketTest.Server` | Demo server for wrapper features. |

## Install

```bash
NuGet\Install-Package CodeWF.NetWeaver
```

## Repository Baseline

- Development SDK: `.NET 11` preview, pinned through `global.json`
- Package management: centralized with `Directory.Packages.props`
- Core libraries: `CodeWF.NetWeaver` and `CodeWF.NetWrapper`
- Sample UI stack: `Avalonia 12.0.2`, `Semi.Avalonia 12.0.1`, `ReactiveUI.Avalonia 12.0.1`
- Free-only policy: `Prism.DryIoc.Avalonia` remains pinned to `8.1.97.11073`
- Grid migration: the sample applications now use `CodeWF.AvaloniaControls.ProDataGrid` instead of the old free `Avalonia.Controls.DataGrid` line

## Build And Scripts

Restore, build, and test the whole solution:

```bash
dotnet restore CodeWF.NetWeaver.slnx
dotnet build CodeWF.NetWeaver.slnx -c Debug
dotnet test CodeWF.NetWeaver.slnx -c Debug --no-build
```

Pack the NuGet libraries:

```bash
pack.bat
```

Publish the runnable samples:

```bash
publish_all.bat
```

## Packet Model

Each packet contains a fixed header plus the serialized object body:

```text
Header
- BufferLen: int
- SystemId: long
- ObjectId: ushort
- ObjectVersion: byte
- UnixTimeMilliseconds: long

Body
- Serialized object payload
```

Mark a DTO with `NetHead` so the serializer can identify it on the wire:

```csharp
using CodeWF.NetWeaver.Base;

[NetHead(10, 1)]
public class ResponseProcessList : INetObject
{
    public int TaskId { get; set; }

    public int TotalSize { get; set; }

    public List<ProcessItem>? Processes { get; set; }
}

public record ProcessItem
{
    public int Pid { get; set; }

    public string? Name { get; set; }
}
```

## Basic Usage

Serialize:

```csharp
var netObject = new ResponseProcessList
{
    TaskId = 3,
    TotalSize = 2,
    Processes =
    [
        new ProcessItem { Pid = 1, Name = "CodeWF.NetWeaver" },
        new ProcessItem { Pid = 2, Name = "CodeWF.NetWrapper" }
    ]
};

var buffer = netObject.Serialize(systemId: 32);
```

Deserialize:

```csharp
var deserialized = buffer.Deserialize<ResponseProcessList>();

Console.WriteLine(deserialized.TotalSize);
```

## CodeWF.NetWrapper

`CodeWF.NetWrapper` handles TCP/UDP communication and converts raw packets into typed commands:

```csharp
using CodeWF.EventBus;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;

var server = new TcpSocketServer();
await server.StartAsync("Server", "0.0.0.0", 8888);

EventBus.Default.Subscribe<SocketCommand>(async (sender, command) =>
{
    // Non-file-management commands can be processed here.
});
```

## File Transfer And File Management

`TcpSocketClient` and `TcpSocketServer` now expose a clearer file management API:

Every request/response-style communication object carries a `TaskId`.
The same `TaskId` is propagated through request, response, chunk data, chunk acknowledgement, reject, and complete messages so callers can correlate a full transfer flow reliably.

### Client API

```csharp
await client.BrowseFileSystemAsync("/");
await client.CreateDirectoryAsync("uploads");
await client.DeletePathAsync("old-folder", true);
await client.DeletePathAsync("uploads/old.bin", false);
await client.UploadFileAsync(@"D:\local\demo.zip", "uploads/demo.zip");
await client.DownloadFileAsync("uploads/demo.zip", @"D:\downloads");
```

### Server Configuration

```csharp
var server = new TcpSocketServer
{
    // All browse/create/delete/upload/download operations are restricted to this root.
    FileSaveDirectory = @"D:\ServerFiles"
};

server.FileTransferProgress += (sender, args) =>
{
    Console.WriteLine($"{args.FileName}: {args.Progress:F2}%");
};
```

### Managed Root Behavior

When `FileSaveDirectory` is set:

- Browsing, creating, deleting, uploading, and downloading are restricted to that root directory.
- Relative paths such as `"uploads/demo.zip"` are resolved under the managed root.
- Attempts to escape the root with paths like `"..\\outside.txt"` are rejected by the server.

### Transfer Behavior

- Transfers use 64 KB blocks.
- Upload and download both support resume by offset.
- Completed files are verified with SHA-256.
- `FileTransferProgress` is raised on both client and server sides.

## File Management DTOs

The file management feature now uses the cleaned-up names below:

| Category | Type |
| --- | --- |
| Browse request | `BrowseFileSystemRequest` |
| Browse response | `BrowseFileSystemResponse` |
| Drive list response | `DriveListResponse` |
| Create directory request | `CreateDirectoryRequest` |
| Create directory response | `CreateDirectoryResponse` |
| Delete path request | `DeletePathRequest` |
| Delete path response | `DeletePathResponse` |

## File Transfer DTOs

The file transfer pipeline now uses these names:

| Category | Type |
| --- | --- |
| Upload request | `FileUploadRequest` |
| Upload response | `FileUploadResponse` |
| Download request | `FileDownloadRequest` |
| Download response | `FileDownloadResponse` |
| Chunk data | `FileChunkData` |
| Chunk acknowledgement | `FileChunkAck` |
| Transfer reject | `FileTransferReject` |
| Transfer complete | `FileTransferComplete` |

## Tests

The repository includes integration-style tests for the wrapper file pipeline:

- upload into the managed server root
- download into the requested local directory
- reject path traversal outside `FileSaveDirectory`

Run them with:

```bash
dotnet test src\CodeWF.NetWrapper.Tests\CodeWF.NetWrapper.Tests.csproj
```

## Design Notes

The design note document is available here:

[CodeWF-NetWeaver-Design-Principles.md](CodeWF-NetWeaver-Design-Principles.md)
