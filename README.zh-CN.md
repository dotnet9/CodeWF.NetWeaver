# CodeWF.NetWeaver

[![NuGet](https://img.shields.io/nuget/v/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.NetWeaver)](LICENSE)

`CodeWF.NetWeaver` 是网络数据包序列化与反序列化的核心库。

`CodeWF.NetWrapper` 构建在它之上，提供 TCP/UDP 帮助类、命令分发，以及文件传输 / 文件管理能力。

英文版文档： [README.md](README.md)

## 项目组成

| 项目 | 说明 |
| --- | --- |
| `CodeWF.NetWeaver` | 核心数据包序列化 / 反序列化库。 |
| `CodeWF.NetWrapper` | 基于 `CodeWF.NetWeaver` 的 TCP/UDP Socket 帮助库。 |
| `SocketTest.Client` | Wrapper 功能演示客户端。 |
| `SocketTest.Server` | Wrapper 功能演示服务端。 |

## 安装

```bash
NuGet\Install-Package CodeWF.NetWeaver
```

## 仓库基线

- 开发 SDK：`.NET 11` 预览版，通过 `global.json` 锁定
- 包管理方式：使用 `Directory.Packages.props` 统一做中央包管理
- 核心类库：`CodeWF.NetWeaver` 与 `CodeWF.NetWrapper`
- 示例 UI 技术栈：`Avalonia 12.0.2`、`Semi.Avalonia 12.0.1`、`ReactiveUI.Avalonia 12.0.1`
- 免费策略：`Prism.DryIoc.Avalonia` 固定为最后一个免费可用的 `8.1.97.11073`
- 表格迁移：示例工程已从旧版免费 `Avalonia.Controls.DataGrid` 链路切换到 `CodeWF.AvaloniaControls.ProDataGrid`

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
```

反序列化：

```csharp
var deserialized = buffer.Deserialize<ResponseProcessList>();

Console.WriteLine(deserialized.TotalSize);
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
