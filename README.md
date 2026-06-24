# CodeWF.NetWeaver

[![NuGet](https://img.shields.io/nuget/v/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.NetWeaver)](LICENSE)

`CodeWF.NetWeaver` 是网络数据包二进制序列化与反序列化核心库。
`CodeWF.NetWrapper` 构建在它之上，提供通用 TCP/UDP 帮助类和命令分发能力。
`CodeWF.NetWrapper.FileSystem` 是依赖 `CodeWF.NetWrapper` 的文件系统扩展包，提供远程文件系统管理、文件上传/下载和断点续传能力。

## 文档职责

- 本 `README.md` 是仓库入口：介绍项目组成、安装、基础用法、构建测试和常用 API。
- [CodeWF.NetWeaver 设计原理与架构说明](docs/CodeWF.NetWeaver设计原理与架构说明.md) 面向维护者：解释协议结构、序列化实现、Socket 封装和文件传输设计。
- `UpdateLog.md` 记录版本更新。
- `文件服务简单流程.md` 保留文件服务流程备忘。

## 仓库规范

- 当前版本：`3.0.0`，版本号统一维护在根目录 `Directory.Build.props` 的 `<Version>` 节点。
- NuGet 包项目支持 `net8.0;net10.0`；示例、测试与内部应用项目使用 `net11.0` / `net11.0-windows`。
- 根目录 `logo.svg`、`logo.png`、`logo.ico` 是唯一图标源，子工程通过 MSBuild `Link` 引用。
- 使用 `Directory.Packages.props` 做中央包管理，并启用传递包 pin。

## 项目组成

| 项目 | 说明 |
| --- | --- |
| `CodeWF.NetWeaver` | 核心数据包序列化 / 反序列化库。 |
| `CodeWF.NetWrapper` | 基于 `CodeWF.NetWeaver` 的通用 TCP/UDP Socket 与命令分发封装库。 |
| `CodeWF.NetWrapper.FileSystem` | 依赖 `CodeWF.NetWrapper` 的远程文件管理与文件传输扩展库。 |
| `SocketDto` | 示例协议 DTO 与对象 ID 常量。 |
| `SocketTest.Client` | Avalonia 示例客户端。 |
| `SocketTest.Server` | Avalonia 示例服务端。 |

## 安装

仅安装数据包序列化核心：

```bash
dotnet add package CodeWF.NetWeaver
```

需要 TCP/UDP 命令分发时，再安装通用封装层：

```bash
dotnet add package CodeWF.NetWrapper
```

需要远程文件管理或文件上传/下载时，安装文件系统扩展包：

```bash
dotnet add package CodeWF.NetWrapper.FileSystem
```

Package Manager Console：

```powershell
NuGet\Install-Package CodeWF.NetWeaver
NuGet\Install-Package CodeWF.NetWrapper
NuGet\Install-Package CodeWF.NetWrapper.FileSystem
```

## 构建与测试

```bash
dotnet restore CodeWF.NetWeaver.slnx
dotnet build CodeWF.NetWeaver.slnx -c Debug
dotnet test CodeWF.NetWeaver.slnx -c Debug --no-build
```

打包 NuGet 类库：

```bash
pack.bat
```

发布示例程序：

```bash
publish_all.bat
```

运行示例时先启动服务端，再启动客户端：

```bash
dotnet run --project src/SocketTest.Server/SocketTest.Server.csproj -f net11.0-windows
dotnet run --project src/SocketTest.Client/SocketTest.Client.csproj -f net11.0-windows
```

## 数据包模型

每个网络数据包由固定头部和对象正文组成：

```text
Header
- BufferLen: int
- SystemId: long
- ObjectId: ushort
- ObjectVersion: byte
- UnixTimeMilliseconds: long

Body
- 序列化后的对象正文
```

通过 `NetHead` 为 DTO 标记协议对象 ID 和版本：

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

`SerializeHelper` 支持基础值类型、可空值类型、字符串、枚举、数组、`List<T>` / 集合接口、`Dictionary<TKey,TValue>` / 字典接口和嵌套对象。需要跳过字段时使用 `NetIgnoreMemberAttribute`。

基础类型支持：

| 类别 | 类型 |
| --- | --- |
| 布尔与字符 | `bool`、`char` |
| 整数 | `byte`、`sbyte`、`short`、`ushort`、`int`、`uint`、`long`、`ulong`、`nint` / `IntPtr`、`nuint` / `UIntPtr`、`Int128`、`UInt128` |
| 浮点与数值 | `Half`、`float`、`double`、`decimal` |
| 时间与标识 | `DateTime`、`DateTimeOffset`、`DateOnly`、`TimeOnly`、`TimeSpan`、`Guid` |
| 其他 | `string`、`enum` |

可空值类型 `Nullable<T>` / `T?` 使用 1 字节 `HasValue` 标记。空值只写入 1 字节；非空值写入 1 字节标记后再写入底层 `T` 的内容。因此已有非可空字段包体大小不变，新增或改用可空字段时需要双端使用相同包版本和 DTO 定义。

Native AOT / trimming 场景下，基础类型读写不依赖动态代码生成。DTO 和嵌套对象仍按公开属性反射读写，目标应用需要保留参与序列化的 DTO 元数据；集合属性在 AOT 场景下建议声明为具体 `List<T>` / `Dictionary<TKey,TValue>`，避免接口集合回退实现需要运行时构造泛型集合。

## CodeWF.NetWrapper

`CodeWF.NetWrapper` 负责 TCP/UDP 通信，并将原始数据包转换为强类型命令。通用包只内置心跳、通用响应和 Socket 连接管理；文件系统等业务协议通过扩展包注册命令处理器：

```csharp
using CodeWF.EventBus;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;

var server = new TcpSocketServer();
await server.StartAsync("Server", "0.0.0.0", 8888);

EventBus.Default.Subscribe<SocketCommand>(async (sender, command) =>
{
    // 未被扩展处理器消费的业务命令可在这里处理。
    await Task.CompletedTask;
});
```

## 文件管理与文件传输

客户端常用 API：

```csharp
using CodeWF.NetWrapper.Helpers;

var fileClient = client.UseFileSystem();

await fileClient.BrowseFileSystemAsync(string.Empty);
await fileClient.BrowseFileSystemAsync(@"D:\ServerFiles");
await fileClient.CreateDirectoryAsync(@"D:\ServerFiles\uploads");
await fileClient.DeletePathAsync(@"D:\ServerFiles\uploads\old.bin", isDirectory: false);
await fileClient.UploadFileAsync(@"D:\local\demo.zip", @"D:\ServerFiles\uploads\demo.zip");
await fileClient.DownloadFileAsync(@"D:\ServerFiles\uploads\demo.zip", @"D:\downloads");
```

服务端文件系统能力通过 `IManagedFileSystem` 抽象，默认使用物理文件系统：

```csharp
var server = new TcpSocketServer();
var fileServer = server.UseFileSystem();

fileServer.FileTransferProgress += (sender, args) =>
{
    Console.WriteLine($"{args.FileName}: {args.Progress:F2}%");
};
```

`UseFileSystem()` 会把文件系统扩展注册到通用 Socket 命令管线。扩展包迁出不改变现有文件协议 DTO 的字段布局、对象 ID 或对象版本，因此数据包大小不受拆包影响；变化的是 NuGet 包和程序集边界。当前实现要求服务端路径使用绝对路径；空路径用于浏览入口，服务端可返回磁盘列表。文件浏览响应按页返回目录条目，上传和下载都通过 `TaskId` 关联同一次传输流程。

传输行为：

- 文件块大小为 64 KB。
- 上传和下载支持断点续传。
- 完成后执行 SHA-256 校验。
- 客户端触发 `FileTransferProgress` 和 `FileTransferOutcome` 事件。
- 服务端触发 `FileTransferProgress` 事件。
- 传输拒绝、文件不存在、路径访问失败、哈希不一致等情况通过 `FileTransferReject` 表达。

## 文件管理 DTO

| 类别 | 类型 |
| --- | --- |
| 浏览请求 | `BrowseFileSystemRequest` |
| 浏览响应 | `BrowseFileSystemResponse` |
| 磁盘列表响应 | `DriveListResponse` |
| 磁盘信息 | `DiskInfo` |
| 文件系统条目 | `FileSystemEntry` |
| 创建目录请求 | `CreateDirectoryRequest` |
| 创建目录响应 | `CreateDirectoryResponse` |
| 删除路径请求 | `DeletePathRequest` |
| 删除路径响应 | `DeletePathResponse` |

## 文件传输 DTO

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

Wrapper 文件链路已有测试覆盖：

- 上传文件到服务端文件系统。
- 下载文件到指定本地目录。
- 拒绝不存在文件、文件大小冲突和哈希冲突等异常路径。

运行方式：

```bash
dotnet test src\CodeWF.NetWrapper.Tests\CodeWF.NetWrapper.Tests.csproj
```

## 第三方开源组件审计

检查范围包括 NuGet 元数据、恢复后的 `project.assets.json`、NuGet.org 信息以及上游源码/许可证链接。优先接受 MIT / Apache-2.0 / BSD。

当前关键依赖：

| 包 | 协议 | 源码/项目地址 | 结论 |
| --- | --- | --- | --- |
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Fonts.Inter` / `Avalonia.Markup.Xaml.Loader` | MIT | https://github.com/AvaloniaUI/Avalonia | 通过，当前使用 `12.0.4` |
| `CodeWF.EventBus` / `CodeWF.Log.Core` / `CodeWF.LogViewer.Avalonia` / `CodeWF.AvaloniaControls.ProDataGrid.Themes` / `CodeWF.Tools.Core` / `CodeWF.Tools.Files` | MIT | CodeWF 自研仓库 | 通过 |
| `Lorem.Universal.Net` | MIT | https://github.com/trichards57/Lorem.Universal.NET | 示例依赖，通过 |
| `Microsoft.NET.Test.Sdk` | MIT | https://github.com/microsoft/vstest | 测试依赖，通过 |
| `Prism.DryIoc.Avalonia` | MIT | https://github.com/AvaloniaCommunity/Prism.Avalonia | 通过，固定到 `8.1.97.11073` |
| `ProDataGrid` | MIT | https://github.com/wieslawsoltes/ProDataGrid | 通过 |
| `ReactiveUI.Avalonia` | MIT | https://github.com/reactiveui/reactiveui | 通过 |
| `Semi.Avalonia` | MIT | https://github.com/irihitech/Semi.Avalonia | 通过，仅使用开源主体包 |
| `System.Configuration.ConfigurationManager` / `System.Drawing.Common` / `System.Security.Cryptography.ProtectedData` / `System.Security.Permissions` / `System.Windows.Extensions` | MIT | https://github.com/dotnet/dotnet | 通过，固定到 `10.0.8` |
| `coverlet.collector` | MIT | https://github.com/coverlet-coverage/coverlet | 测试依赖，通过 |
| `xunit` / `xunit.runner.visualstudio` | Apache-2.0 | https://github.com/xunit/xunit | 测试依赖，通过 |

示例工程已移除 `Semi.Avalonia.ProDataGrid` 和 `AvaloniaUI.DiagnosticsSupport`，表格链路改用 MIT 的 `ProDataGrid` 与自研开源主题包。
## 包版本维护约定

XML 文件统一使用两个空格缩进。`Directory.Packages.props` 统一承载 NuGet 中央包管理开关和包版本变量，包括 `AvaloniaVersion` 等共享版本属性；`Directory.Build.props` 仅保留项目构建、编译选项和 NuGet 元数据。仓库如引用 `VC-LTL`、`YY-Thunks`，这两个兼容旧版操作系统的特殊包必须使用最新预览版。
