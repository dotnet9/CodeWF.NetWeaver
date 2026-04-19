# CodeWF.NetWeaver

[![NuGet](https://img.shields.io/nuget/v/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.NetWeaver)](LICENSE)

CodeWF.NetWeaver 是一个简洁而强大的 C# 核心序列化库，支持 AOT，用于处理 TCP 和 UDP 数据包的组包和解包操作。

CodeWF.NetWeaver is a concise and powerful C# core serialization library that supports AOT for handling TCP and UDP packet grouping and unpacking operations.

## 框架组成

| 项目                  | 说明                                                       |
| --------------------- | ---------------------------------------------------------- |
| **CodeWF.NetWeaver**  | 核心序列化库，负责数据包的组包和解包（本文档）             |
| **CodeWF.NetWrapper** | TCP/UDP Socket 封装库，基于 NetWeaver 实现高级网络通信功能 |

## 安装(Installer)

```bash
NuGet\Install-Package CodeWF.NetWeaver -Version 1.3.0
```

## 核心概念

### 数据包结构

```
┌────────────────────────────────────────────┐
│  Header (23 bytes)                         │
│  ├── BufferLen: int                        │
│  ├── SystemId: long                        │
│  ├── ObjectId: ushort                      │
│  ├── ObjectVersion: byte                   │
│  └── UnixTimeMs: long                      │
├────────────────────────────────────────────┤
│  Body (可变长度)                            │
└────────────────────────────────────────────┘
```

### 特性系统

| 特性                                    | 用途                     |
| --------------------------------------- | ------------------------ |
| `NetHeadAttribute(id, version)`         | 标记网络对象类型和版本   |
| `NetIgnoreMemberAttribute`              | 序列化时忽略该成员       |
| `NetFieldOffsetAttribute(offset, size)` | 位字段打包，节省存储空间 |

### 常量系统

所有对象 ID 均定义为常量，便于统一管理和维护：

```csharp
// SocketDto/NetConsts.cs
public class NetConsts
{
    // 响应对象 ID
    public const ushort ResponseProcessListObjectId = 10;
    public const ushort ResponseServiceInfoObjectId = 6;

    // 请求对象 ID
    public const ushort RequestProcessListObjectId = 5;
    public const ushort RequestServiceInfoObjectId = 9;

    // UDP 对象 ID
    public const ushort UpdateRealtimeProcessListObjectId = 200;
    public const ushort UpdateGeneralProcessListObjectId = 201;
}
```

## 定义通信对象(Define Net Object)

```csharp
// 使用常量代替魔数
[NetHead(NetConsts.ResponseProcessListObjectId, 1)]
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

    public byte Type { get; set; }

    public uint LastUpdateTime { get; set; }
}
```

## 使用(Usage)

### 序列化

```csharp
var netObject = new ResponseProcessList
{
    TaskId = 3,
    TotalSize = 200,
    Processes = new List<ProcessItem>
    {
        new ProcessItem { Pid = 1, Name = "CodeWF.NetWeaver", Type = 1 }
    }
};

// 序列化：对象 -> 字节数组
var buffer = netObject.Serialize(systemId: 32);
```

### 反序列化

```csharp
// 反序列化：字节数组 -> 对象
var desObject = buffer.Deserialize<ResponseProcessList>();

Assert.Equal(netObject.TotalSize, desObject.TotalSize);
```

## 与 CodeWF.NetWrapper 的关系

```csharp
// CodeWF.NetWeaver 只负责序列化和反序列化
var buffer = myObject.Serialize(systemId);
var obj = buffer.Deserialize<MyObject>();

// CodeWF.NetWrapper 提供完整的 TCP/UDP Socket 封装
var server = new TcpSocketServer();
await server.StartAsync("Server", "0.0.0.0", 8888);

// Socket 收到数据后自动序列化为对象
EventBus.Default.Subscribe<SocketCommand>(async (sender, cmd) =>
{
    var myObject = cmd.GetCommand<MyObject>();
    // 处理业务逻辑
});
```

详细设计原理请参阅：[CodeWF-NetWeaver-Design-Principles.md](CodeWF-NetWeaver-Design-Principles.md)

---

## 文件传输功能

### 文件传输特性

CodeWF.NetWrapper 内置了完整的文件传输功能，支持：

- **断点续传**：网络中断后可继续传输
- **分块传输**：64KB 块大小，高效传输
- **SHA256 校验**：确保文件完整性
- **进度通知**：实时传输进度更新

### 文件传输协议流程

```
上传流程：
1. 客户端 → 服务器：FileTransferStart（包含文件信息）
2. 服务器 → 客户端：FileTransferStartAck（确认接收）
3. 客户端 → 服务器：FileBlockData（逐个发送数据块）
4. 服务器 → 客户端：FileBlockAck（确认块接收）
5. 客户端 → 服务器：FileTransferComplete（传输完成）

下载流程：
1. 客户端 → 服务器：FileTransferStart（请求下载）
2. 服务器 → 客户端：FileTransferStartAck（确认发送）
3. 服务器 → 客户端：FileBlockData（逐个发送数据块）
4. 客户端 → 服务器：FileBlockAck（确认块接收）
5. 服务器 → 客户端：FileTransferComplete（传输完成）
```

### 文件传输相关模型

| 模型                            | 说明                  |
| ------------------------------- | --------------------- |
| `FileTransferStart`             | 文件传输开始请求/响应 |
| `FileTransferStartAck`          | 文件传输开始应答      |
| `FileBlockData`                 | 文件块数据            |
| `FileBlockAck`                  | 文件块传输应答        |
| `FileTransferComplete`          | 文件传输完成          |
| `FileTransferSession`           | 文件传输会话状态      |
| `FileTransferProgressEventArgs` | 文件传输进度事件参数  |

### 客户端使用示例

```csharp
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;

var client = new TcpSocketClient();
await client.ConnectAsync("TestClient", "127.0.0.1", 8888);

// 订阅文件传输进度
client.FileTransferProgress += (sender, args) =>
{
    Console.WriteLine($"传输进度：{args.Progress:F2}% ({args.TransferredBytes}/{args.TotalBytes}字节)");
};

// 上传文件
await client.StartFileUploadAsync("D:\\test.zip", "server_test.zip");

// 下载文件
await client.StartFileDownloadAsync("server_test.zip", "D:\\download_test.zip");
```

### 服务端使用示例

```csharp
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using EventBus.Core;

var server = new TcpSocketServer();
await server.StartAsync("TestServer", "0.0.0.0", 8888);

// 文件保存目录
server.FileSaveDirectory = "D:\\ServerFiles";

// 服务端同样支持文件传输进度
server.FileTransferProgress += (sender, args) =>
{
    Logger.Info($"{args.FileName}：{args.Progress:F2}%");
};

EventBus.Default.Subscribe<SocketCommand>(async (sender, cmd) =>
{
    // 文件传输相关命令会自动处理，无需额外编码
    if (cmd.IsCommand<FileTransferStart>())
    {
        // 可选：自定义处理文件传输开始
    }
});
```

### 断点续传原理

断点续传通过以下方式实现：

1. 传输前检查本地是否已有部分文件
2. 计算已传输字节数 `AlreadyTransferredBytes`
3. 文件开始传输时告知对端已传输位置
4. 从断点处继续发送剩余数据块
5. 完成后校验 SHA256 哈希值确保完整性
