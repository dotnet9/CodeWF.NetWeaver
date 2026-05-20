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

Install only the packet serializer:

```bash
dotnet add package CodeWF.NetWeaver
```

Install the socket helper layer when you need TCP/UDP command dispatching, file transfer, or file management:

```bash
dotnet add package CodeWF.NetWrapper
```

Package Manager Console is also supported:

```powershell
NuGet\Install-Package CodeWF.NetWeaver
```

## Repository Baseline

- Development SDK: `.NET 11` preview, pinned through `global.json`
- Package management: centralized with `Directory.Packages.props`
- Core libraries: `CodeWF.NetWeaver` and `CodeWF.NetWrapper`
- Sample UI stack: `Avalonia 12.0.3`, `Semi.Avalonia 12.0.1`, `ReactiveUI.Avalonia 12.0.1`
- Free-only policy: `Prism.DryIoc.Avalonia` remains pinned to `8.1.97.11073`
- Grid migration: the sample applications now use the open-source `ProDataGrid` package and the in-house open `CodeWF.AvaloniaControls.ProDataGrid.Themes` package instead of the old free `Avalonia.Controls.DataGrid` line or non-open Semi ProDataGrid theme package

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

Run the server sample first, then the client sample:

```bash
dotnet run --project src/SocketTest.Server/SocketTest.Server.csproj -f net11.0-windows
dotnet run --project src/SocketTest.Client/SocketTest.Client.csproj -f net11.0-windows
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
Console.WriteLine($"Packet bytes: {buffer.Length}");
```

Deserialize:

```csharp
var deserialized = buffer.Deserialize<ResponseProcessList>();

Console.WriteLine($"Process count: {deserialized.Processes?.Count ?? 0}");
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
    await Task.CompletedTask;
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

## Third-Party Open Source Audit

Checked on 2026-05-20 with NuGet metadata, restored `project.assets.json`, and upstream source/license links. MIT / Apache-2.0 / BSD are preferred.

Remediation:

- Removed `Semi.Avalonia.ProDataGrid` from sample projects and switched to MIT `ProDataGrid` plus the in-house open `CodeWF.AvaloniaControls.ProDataGrid.Themes` package.
- Removed `AvaloniaUI.DiagnosticsSupport` because the package does not publish a clear open-source license or source repository.
- Replaced sample/test references to aggregate `CodeWF.Tools` with `CodeWF.Tools.Core` / `CodeWF.Tools.Files` so the samples no longer pull the image/Magick dependency chain unless they need it.
- Enabled transitive package pinning and pinned `System.Configuration.ConfigurationManager`, `System.Drawing.Common`, `System.Security.Cryptography.ProtectedData`, `System.Security.Permissions`, and `System.Windows.Extensions` to `10.0.8`, removing old `4.7.0` transitive dependency chains.
- Updated the approved package line to `Avalonia 12.0.3`, `CodeWF.EventBus 3.4.5.5`, `CodeWF.LogViewer.Avalonia 12.0.3.1`, `CodeWF.AvaloniaControls.ProDataGrid.Themes 12.0.3.2`, `CodeWF.Tools.Core` / `CodeWF.Tools.Files 1.3.13.2`, and `coverlet.collector 10.0.1`.

| Package | License | Source | Status |
| --- | --- | --- | --- |
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Fonts.Inter` / `Avalonia.Markup.Xaml.Loader` | MIT | https://github.com/AvaloniaUI/Avalonia | Approved, `12.0.3` |
| `CodeWF.EventBus` / `CodeWF.Log.Core` / `CodeWF.LogViewer.Avalonia` / `CodeWF.AvaloniaControls.ProDataGrid.Themes` / `CodeWF.Tools.Core` / `CodeWF.Tools.Files` | MIT | CodeWF repositories | Own open-source packages; using `CodeWF.EventBus 3.4.5.5`, `CodeWF.Log.Core 12.0.3.1`, `CodeWF.LogViewer.Avalonia 12.0.3.1`, `CodeWF.AvaloniaControls.ProDataGrid.Themes 12.0.3.2`, `CodeWF.Tools.* 1.3.13.2` |
| `Lorem.Universal.Net` | MIT | https://github.com/trichards57/Lorem.Universal.NET | Approved, sample-only |
| `Microsoft.NET.Test.Sdk` | MIT | https://github.com/microsoft/vstest | Approved, test-only |
| `Prism.DryIoc.Avalonia` | MIT | https://github.com/AvaloniaCommunity/Prism.Avalonia | Approved, pinned to 8.x |
| `ProDataGrid` | MIT | https://github.com/wieslawsoltes/ProDataGrid | Approved |
| `ReactiveUI.Avalonia` | MIT | https://github.com/reactiveui/reactiveui | Approved |
| `Semi.Avalonia` | MIT | https://github.com/irihitech/Semi.Avalonia | Approved, only the open core package is used |
| `System.Configuration.ConfigurationManager` / `System.Drawing.Common` / `System.Security.Cryptography.ProtectedData` / `System.Security.Permissions` / `System.Windows.Extensions` | MIT | https://github.com/dotnet/dotnet | Approved, pinned to `10.0.8` |
| `coverlet.collector` | MIT | https://github.com/coverlet-coverage/coverlet | Approved, `10.0.1`, test-only |
| `xunit` / `xunit.runner.visualstudio` | Apache-2.0 | https://github.com/xunit/xunit | Approved, test-only |

Transitive dependencies from Avalonia/SkiaSharp/ANGLE, ProDataGrid, CodeWF.AvaloniaControls.ProDataGrid.Themes, CodeWF.Tools.Files (`CsvHelper`, `MiniExcel`, `SharpCompress`, `YamlDotNet`), Prism.Avalonia, and ReactiveUI were checked and are source-open. Effective restore assets no longer contain `Semi.Avalonia.ProDataGrid`, `AvaloniaUI.DiagnosticsSupport`, `Magick.NET-Q16-AnyCPU`, `System.Drawing.Common 4.7.0`, `System.Configuration.ConfigurationManager 4.7.0`, or `System.Security.Cryptography.ProtectedData 4.7.0`.
