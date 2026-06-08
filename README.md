# CodeWF.NetWeaver

[![NuGet](https://img.shields.io/nuget/v/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.NetWeaver)](LICENSE)

`CodeWF.NetWeaver` 是网络数据包序列化与反序列化的核心库。

`CodeWF.NetWrapper` 构建在它之上，提供 TCP/UDP 帮助类、命令分发，以及文件传输 / 文件管理能力。

## 仓库规范

- 当前版本：`2.1.2.3`，版本号统一维护在根目录 `Directory.Build.props` 的 `<Version>` 节点。
- NuGet 包项目统一支持 `net8.0;net10.0`；Demo、App、测试与内部应用项目统一使用 `net11.0` / `net11.0-windows`。
- 根目录 `logo.svg`、`logo.png`、`logo.ico` 是唯一图标源，子工程只通过 MSBuild `Link` 引用，不维护图标副本。
- 运行时帮助、Markdown 示例、内置备忘录、设计说明等业务文档按功能保留；仓库级入口文档使用根目录 `README.md` 和 `UpdateLog.md`。

## 项目组成

| 项目 | 说明 |
| --- | --- |
| `CodeWF.NetWeaver` | 核心数据包序列化 / 反序列化库。 |
| `CodeWF.NetWrapper` | 基于 `CodeWF.NetWeaver` 的 TCP/UDP Socket 帮助库。 |
| `SocketTest.Client` | Wrapper 功能演示客户端。 |
| `SocketTest.Server` | Wrapper 功能演示服务端。 |

## 安装

仅安装数据包序列化核心：

```bash
dotnet add package CodeWF.NetWeaver
```

如果需要 TCP/UDP 命令分发、文件传输或文件管理能力，再安装封装层：

```bash
dotnet add package CodeWF.NetWrapper
```

也可以在 Package Manager Console 中安装：

```powershell
NuGet\Install-Package CodeWF.NetWeaver
```

## 仓库基线

- 开发 SDK：使用本机或 CI 环境安装的 .NET SDK，不再维护 `global.json`
- 包管理方式：使用 `Directory.Packages.props` 统一做中央包管理
- 核心类库：`CodeWF.NetWeaver` 与 `CodeWF.NetWrapper`
- 示例 UI 技术栈：`Avalonia 12.0.3`、`Semi.Avalonia 12.0.1`、`ReactiveUI.Avalonia 12.0.1`
- 免费策略：`Prism.DryIoc.Avalonia` 固定为最后一个免费可用的 `8.1.97.11073`
- 表格迁移：示例工程已从旧版免费 `Avalonia.Controls.DataGrid` 链路切换到开源 `ProDataGrid` 与自研开源 `CodeWF.AvaloniaControls.ProDataGrid.Themes`，不再使用非开源 Semi ProDataGrid 主题包

## 构建与脚本

还原、构建并测试整个解决方案：

```bash
dotnet restore CodeWF.NetWeaver.slnx
dotnet build CodeWF.NetWeaver.slnx -c Debug
dotnet test CodeWF.NetWeaver.slnx -c Debug --no-build
```

打包 NuGet 类库：

```bash
pack.bat
```

发布可运行示例：

```bash
publish_all.bat
```

运行示例时先启动服务端，再启动客户端：

```bash
dotnet run --project src/SocketTest.Server/SocketTest.Server.csproj -f net11.0-windows
dotnet run --project src/SocketTest.Client/SocketTest.Client.csproj -f net11.0-windows
```

## 数据包模型

每个数据包由固定头部和对象正文两部分组成：

```text
Header
- BufferLen: int
- SystemId: long
- ObjectId: ushort
- ObjectVersion: byte
- UnixTimeMilliseconds: long

Body
- 序列化后的对象负载
```

可以通过 `NetHead` 为 DTO 标记协议头信息：

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

## 基础用法

序列化：

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

反序列化：

```csharp
var deserialized = buffer.Deserialize<ResponseProcessList>();

Console.WriteLine($"Process count: {deserialized.Processes?.Count ?? 0}");
```

## CodeWF.NetWrapper

`CodeWF.NetWrapper` 负责 TCP/UDP 通信，并将原始数据包转换为强类型命令：

```csharp
using CodeWF.EventBus;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;

var server = new TcpSocketServer();
await server.StartAsync("Server", "0.0.0.0", 8888);

EventBus.Default.Subscribe<SocketCommand>(async (sender, command) =>
{
    // 非文件管理命令可以在这里处理。
    await Task.CompletedTask;
});
```

## 文件传输与文件管理

`TcpSocketClient` 和 `TcpSocketServer` 现在使用更清晰的文件管理接口。

每个请求 / 响应式通信对象都带有 `TaskId`。同一个传输流程中的请求、响应、分块数据、分块确认、拒绝消息与完成消息都会沿用同一个 `TaskId`，用于把 Response 与对应的 Request 一一关联起来。

### 客户端 API

```csharp
await client.BrowseFileSystemAsync("/");
await client.CreateDirectoryAsync("uploads");
await client.DeletePathAsync("old-folder", true);
await client.DeletePathAsync("uploads/old.bin", false);
await client.UploadFileAsync(@"D:\local\demo.zip", "uploads/demo.zip");
await client.DownloadFileAsync("uploads/demo.zip", @"D:\downloads");
```

### 服务端配置

```csharp
var server = new TcpSocketServer
{
    // 所有浏览、创建、删除、上传、下载操作都会限制在该根目录下。
    FileSaveDirectory = @"D:\ServerFiles"
};

server.FileTransferProgress += (sender, args) =>
{
    Console.WriteLine($"{args.FileName}: {args.Progress:F2}%");
};
```

### 托管根目录行为

设置 `FileSaveDirectory` 后：

- 浏览、创建、删除、上传、下载都会被限制在该根目录下。
- 相对路径例如 `"uploads/demo.zip"` 会解析到这个根目录内。
- 尝试使用 `"..\\outside.txt"` 之类的路径跳出根目录时，服务端会拒绝请求。

### 传输行为

- 传输块大小为 64 KB。
- 上传和下载都支持基于偏移量的断点续传。
- 文件完成后会执行 SHA-256 校验。
- 客户端和服务端都会触发 `FileTransferProgress` 事件。

## 文件管理 DTO

当前文件管理协议对象名称如下：

| 类别 | 类型 |
| --- | --- |
| 浏览请求 | `BrowseFileSystemRequest` |
| 浏览响应 | `BrowseFileSystemResponse` |
| 磁盘列表响应 | `DriveListResponse` |
| 创建目录请求 | `CreateDirectoryRequest` |
| 创建目录响应 | `CreateDirectoryResponse` |
| 删除路径请求 | `DeletePathRequest` |
| 删除路径响应 | `DeletePathResponse` |

## 文件传输 DTO

当前文件传输协议对象名称如下：

| 类别 | 类型 |
| --- | --- |
| 上传请求 | `FileUploadRequest` |
| 上传响应 | `FileUploadResponse` |
| 下载请求 | `FileDownloadRequest` |
| 下载响应 | `FileDownloadResponse` |
| 分块数据 | `FileChunkData` |
| 分块确认 | `FileChunkAck` |
| 传输拒绝 | `FileTransferReject` |
| 传输完成 | `FileTransferComplete` |

## 测试

仓库中已经补充了面向 `CodeWF.NetWrapper` 文件链路的测试：

- 上传文件到服务端托管根目录。
- 下载文件到指定本地目录。
- 拦截越出 `FileSaveDirectory` 的路径访问。

运行方式：

```bash
dotnet test src\CodeWF.NetWrapper.Tests\CodeWF.NetWrapper.Tests.csproj
```

## 设计说明

设计说明文档见：

[CodeWF-NetWeaver-Design-Principles.md](CodeWF-NetWeaver-Design-Principles.md)

## 第三方开源组件审计

检查时间：2026-05-20。检查范围包括 NuGet 元数据、恢复后的 `project.assets.json`、NuGet.org 信息以及上游源码/许可证链接。优先接受 MIT / Apache-2.0 / BSD。

本次整改：

- 示例工程移除 `Semi.Avalonia.ProDataGrid`，改用 MIT 的 `ProDataGrid` 和自研开源 `CodeWF.AvaloniaControls.ProDataGrid.Themes` 包。
- 移除 `AvaloniaUI.DiagnosticsSupport`，因为该包未公开明确的开源许可证和源码仓库。
- 示例/测试中的聚合包 `CodeWF.Tools` 改为按需引用 `CodeWF.Tools.Core` / `CodeWF.Tools.Files`，避免不需要图片能力时引入 Magick 链路。
- 启用传递包 pin，并将 `System.Configuration.ConfigurationManager`、`System.Drawing.Common`、`System.Security.Cryptography.ProtectedData`、`System.Security.Permissions`、`System.Windows.Extensions` 固定到 `10.0.8`，移除旧 `4.7.0` 传递依赖链。
- 已将通过审计的包线更新到 `Avalonia 12.0.3`、`CodeWF.EventBus 3.4.5.5`、`CodeWF.LogViewer.Avalonia 12.0.3.1`、`CodeWF.AvaloniaControls.ProDataGrid.Themes 12.0.3.2`、`CodeWF.Tools.Core` / `CodeWF.Tools.Files 1.3.13.2`、`coverlet.collector 10.0.1`。

| 包 | 协议 | 源码/项目地址 | 结论 |
| --- | --- | --- | --- |
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Fonts.Inter` / `Avalonia.Markup.Xaml.Loader` | MIT | https://github.com/AvaloniaUI/Avalonia | 通过，`12.0.3` |
| `CodeWF.EventBus` / `CodeWF.Log.Core` / `CodeWF.LogViewer.Avalonia` / `CodeWF.AvaloniaControls.ProDataGrid.Themes` / `CodeWF.Tools.Core` / `CodeWF.Tools.Files` | MIT | CodeWF 自研仓库 | 自研开源包；当前使用 `CodeWF.EventBus 3.4.5.5`、`CodeWF.Log.Core 12.0.3.1`、`CodeWF.LogViewer.Avalonia 12.0.3.1`、`CodeWF.AvaloniaControls.ProDataGrid.Themes 12.0.3.2`、`CodeWF.Tools.* 1.3.13.2` |
| `Lorem.Universal.Net` | MIT | https://github.com/trichards57/Lorem.Universal.NET | 示例依赖，通过 |
| `Microsoft.NET.Test.Sdk` | MIT | https://github.com/microsoft/vstest | 测试依赖，通过 |
| `Prism.DryIoc.Avalonia` | MIT | https://github.com/AvaloniaCommunity/Prism.Avalonia | 通过，固定到 8.x 开源线 |
| `ProDataGrid` | MIT | https://github.com/wieslawsoltes/ProDataGrid | 通过 |
| `ReactiveUI.Avalonia` | MIT | https://github.com/reactiveui/reactiveui | 通过 |
| `Semi.Avalonia` | MIT | https://github.com/irihitech/Semi.Avalonia | 通过，仅使用开源主体包 |
| `System.Configuration.ConfigurationManager` / `System.Drawing.Common` / `System.Security.Cryptography.ProtectedData` / `System.Security.Permissions` / `System.Windows.Extensions` | MIT | https://github.com/dotnet/dotnet | 通过，固定到 `10.0.8` |
| `coverlet.collector` | MIT | https://github.com/coverlet-coverage/coverlet | 测试依赖，通过，`10.0.1` |
| `xunit` / `xunit.runner.visualstudio` | Apache-2.0 | https://github.com/xunit/xunit | 测试依赖，通过 |

传递依赖检查结论：Avalonia / SkiaSharp / ANGLE、ProDataGrid、CodeWF.AvaloniaControls.ProDataGrid.Themes、CodeWF.Tools.Files（`CsvHelper`、`MiniExcel`、`SharpCompress`、`YamlDotNet`）、Prism.Avalonia、ReactiveUI 链路均有公开源码。有效恢复资产不再包含 `Semi.Avalonia.ProDataGrid`、`AvaloniaUI.DiagnosticsSupport`、`Magick.NET-Q16-AnyCPU`、`System.Drawing.Common 4.7.0`、`System.Configuration.ConfigurationManager 4.7.0` 或 `System.Security.Cryptography.ProtectedData 4.7.0`。
