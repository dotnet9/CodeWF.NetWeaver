# CodeWF.NetWeaver

[![NuGet](https://img.shields.io/nuget/v/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.NetWeaver.svg)](https://www.nuget.org/packages/CodeWF.NetWeaver/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.NetWeaver)](LICENSE)

CodeWF.NetWeaver 是一个简洁而强大的 C# 核心序列化库，支持 AOT，用于处理 TCP 和 UDP 数据包的组包和解包操作。

CodeWF.NetWeaver is a concise and powerful C# core serialization library that supports AOT for handling TCP and UDP packet grouping and unpacking operations.

## 框架组成

| 项目 | 说明 |
|------|------|
| **CodeWF.NetWeaver** | 核心序列化库，负责数据包的组包和解包（本文档） |
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

| 特性 | 用途 |
|------|------|
| `NetHead(id, version)` | 标记网络对象类型和版本 |
| `NetIgnoreMember` | 序列化时忽略该成员 |
| `NetFieldOffset(offset, size)` | 位字段打包 |

## 定义通信对象(Define Net Object)

```csharp
[NetHead(10, 1)]  // ObjectId = 10, Version = 1
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
