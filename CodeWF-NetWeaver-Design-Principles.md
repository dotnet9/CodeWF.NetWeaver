# CodeWF.NetWeaver 网络通信库设计原理与架构详解

## 概述

CodeWF.NetWeaver 是一个简洁而强大的 C# 网络通信库，分为两个核心项目：

| 项目 | 说明 |
|------|------|
| **CodeWF.NetWeaver** | 核心序列化/反序列化库，支持 AOT，提供数据包组包和解包能力 |
| **CodeWF.NetWrapper** | TCP/UDP Socket 封装库，基于 CodeWF.NetWeaver 实现高级网络通信功能 |

---

## 一、CodeWF.NetWeaver 核心设计原理

### 1.1 项目结构

```
CodeWF.NetWeaver/
├── Base/                           # 基础定义
│   ├── INetObject.cs              # 网络对象接口
│   ├── NetHeadAttribute.cs        # 网络头标记特性
│   ├── NetHeadInfo.cs             # 网络头信息数据结构
│   ├── NetFieldOffsetAttribute.cs # 位字段偏移量特性
│   └── NetIgnoreMemberAttribute.cs # 序列化忽略成员特性
├── SerializeHelper.cs             # 主文件（常量定义）
├── SerializeHelper.Serialize.cs   # 序列化实现
├── SerializeHelper.Deserialize.cs # 反序列化实现
├── SerializeHelper.BitField.cs    # 位字段处理实现
└── SocketHelper.cs                # Socket 辅助方法
```

### 1.2 数据包结构设计

CodeWF.NetWeaver 采用**固定头部 + 可变 body** 的二进制协议设计：

```
┌─────────────────────────────────────────────────────────────┐
│                        数据包结构                             │
├──────────────────┬──────────────────────────────────────────┤
│     字段          │                   说明                     │
├──────────────────┼──────────────────────────────────────────┤
│   BufferLen      │  整个数据包长度（4字节 int）                │
│   SystemId       │  系统标识（8字节 long），用于区分不同服务端    │
│   ObjectId       │  对象类型ID（2字节 ushort），标记消息类型     │
│   ObjectVersion  │  对象版本（1字节 byte），用于协议版本兼容     │
│   UnixTimeMs     │  时间戳（8字节 long），毫秒级 Unix 时间      │
├──────────────────┴──────────────────────────────────────────┤
│                        Body 数据                             │
└─────────────────────────────────────────────────────────────┘
```

**头部固定长度为 23 字节**（`PacketHeadLen = sizeof(int) + sizeof(long) + sizeof(ushort) + sizeof(byte) + sizeof(long)`）

### 1.3 特性系统

库中定义了四个关键特性（Attribute）用于标记网络对象：

| 特性 | 目标 | 用途 |
|------|------|------|
| `NetHeadAttribute` | 类 | 标记网络对象类型，指定 `Id` 和 `Version` |
| `NetFieldOffsetAttribute` | 字段/属性 | 指定位字段的偏移量和大小，用于紧凑二进制结构 |
| `NetIgnoreMemberAttribute` | 字段/属性 | 序列化时忽略该成员 |
| `NetHeadInfo` | 无 | 用于在运行时存储解析出的头部信息 |

**使用示例：**

```csharp
[NetHead(id: 1, version: 1)]  // ObjectId = 1, Version = 1
public class MyMessage : INetObject
{
    public int Id { get; set; }

    [NetIgnoreMember]         // 序列化时忽略此字段
    public string? TempData { get; set; }

    public string Name { get; set; } = string.Empty;
}
```

### 1.4 序列化实现原理

#### 核心机制

1. **反射 + 缓存**：通过 `ConcurrentDictionary` 缓存类型属性信息，避免频繁反射

```csharp
private static readonly ConcurrentDictionary<string, List<PropertyInfo>> ObjectPropertyInfos = new();
```

2. **类型分发**：根据属性类型调用不同的序列化方法

```csharp
if (valueType.IsPrimitive || valueType == typeof(string) || valueType.IsEnum)
    SerializeBaseValue(writer, value, valueType);      // 基本类型
else if (valueType.IsArray)
    SerializeArrayValue(writer, value, valueType);      // 数组
else if (ComplexTypeNames.Contains(valueType.Name))
    SerializeComplexValue(writer, value, valueType);    // List, Dictionary
else
    SerializeProperties(writer, value);                 // 复杂对象（递归）
```

3. **基本类型覆盖**：支持 `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `bool`, `char`, `string`, `enum`

#### 序列化流程

```
Serialize<T>(data, systemId)
    │
    ├─► 获取 NetHeadAttribute（ObjectId + Version）
    │
    ├─► 序列化 Body：SerializeObject(data)
    │       │
    │       ├─► 遍历所有属性（排除 NetIgnoreMember）
    │       │
    │       └─► SerializeValue 根据类型分发
    │
    └─► 组装完整数据包（头部 + body）
```

### 1.5 反序列化实现原理

1. **Span<T> 高性能读取**：使用 `Span<T>` 和 `ReadOnlySpan<T>` 避免内存拷贝

```csharp
public static bool ReadHead(this byte[] buffer, ref int readIndex, out NetHeadInfo netObjectHeadInfo)
{
    if (ReadHead(buffer.AsSpan(readIndex), out netObjectHeadInfo, out var bytesConsumed))
    {
        readIndex += bytesConsumed;
        return true;
    }
    return false;
}
```

2. **动态实例创建**：通过 `Activator.CreateInstance` 和泛型参数类型创建对象

```csharp
public static object CreateInstance(Type type)
{
    var itemTypes = type.GetGenericArguments();
    if (typeof(IList).IsAssignableFrom(type))
    {
        var lstType = typeof(List<>);
        var genericType = lstType.MakeGenericType(itemTypes.First());
        return Activator.CreateInstance(genericType)!;
    }
    // ...
}
```

### 1.6 位字段支持

`SerializeHelper.BitField.cs` 提供了位级别的打包/解包能力，适用于需要紧凑表示的场景（如协议头中的标志位）：

```csharp
// 使用 NetFieldOffsetAttribute 标记位字段
public class Flags
{
    [NetFieldOffset(0, 4)]  // 从第0位开始，占4位
    public byte Type { get; set; }

    [NetFieldOffset(4, 1)]  // 从第4位开始，占1位
    public byte IsValid { get; set; }
}

// 序列化和反序列化
var flags = new Flags { Type = 5, IsValid = 1 };
var buffer = flags.FieldObjectBuffer();  // 序列化为字节数组
var obj = buffer.ToFieldObject<Flags>(); // 从字节数组反序列化
```

---

## 二、CodeWF.NetWrapper 设计原理

### 2.1 项目结构

```
CodeWF.NetWrapper/
├── Commands/                       # 命令/消息定义
│   ├── SocketCommand.cs          # Socket 命令基类
│   ├── TcpClientErrorCommand.cs  # TCP 客户端错误通知
│   └── SocketClientChangedCommand.cs
├── Helpers/                       # Socket 封装实现
│   ├── TcpSocketServer.cs         # TCP 服务器
│   ├── TcpSocketClient.cs        # TCP 客户端
│   ├── TcpSession.cs             # TCP 会话管理
│   ├── UdpSocketServer.cs         # UDP 服务器（组播）
│   ├── UdpSocketClient.cs         # UDP 客户端
│   └── NetHelper.cs              # 网络辅助工具
├── Models/                        # 内置消息模型
│   ├── Heartbeat.cs              # 心跳消息
│   ├── CommonSocketResponse.cs   # 通用响应
│   └── TcpResponseStatus.cs       # 响应状态枚举
└── SocketConstants.cs            # 常量定义
```

### 2.2 TCP 通信架构

#### TcpSocketServer（服务端）

```
TcpSocketServer
    │
    ├── _clients: ConcurrentDictionary<string, TcpSession>
    │                  │ Key: "IP:Port"
    │                  │ Value: TcpSession
    │
    ├── _requests: Channel<(string ClientKey, SocketCommand Command)>
    │                  │ 异步消息队列，包含客户端键和命令
    │
    └── _detectionTimer: PeriodicTimer
                           │ 定期检测客户端超时
```

**核心流程：**

1. **启动监听**：`StartAsync` 创建 `Socket` 监听
2. **接受连接**：`AcceptAsync` 等待客户端连接，创建 `TcpSession`
3. **数据接收**：`ReadPacketAsync` 循环读取完整数据包，写入 Channel
4. **消息分发**：`ProcessingRequestsAsync` 通过 `ReadAllAsync` 消费 Channel，通过 `EventBus` 发布 `SocketCommand` 事件
5. **心跳检测**：定时器定期检测客户端最后活跃时间

#### TcpSocketClient（客户端）

```
TcpSocketClient
    │
    ├── _client: Socket
    │
    └── _responses: Channel<SocketCommand>
                       │ 存储服务器响应
```

**核心流程：**

1. **连接**：`ConnectAsync` 建立 TCP 连接
2. **监听**：`ListenForServerAsync` 循环接收服务器数据
3. **响应处理**：`CheckResponseAsync` 通过 `EventBus` 发布响应事件

#### TcpSession（会话管理）

```csharp
public class TcpSession
{
    public Socket? TcpSocket { get; set; }      // 客户端 Socket
    public CancellationTokenSource? TokenSource { get; set; }  // 取消令牌
    public DateTime? ActiveTime { get; set; }   // 最后活跃时间
}
```

### 2.3 UDP 通信架构（组播）

#### 设计背景

UDP 无连接特性导致无法像 TCP 那样通过 `Accept` 识别客户端。因此库采用**组播（Multicast）**模式：

```
UdpSocketServer (组播发送者)
    │
    └──► 239.0.0.1:8888 (组播地址)
              │
    ┌─────────┼─────────┐
    │         │         │
UdpClient  UdpClient  UdpClient
 (订阅者)   (订阅者)   (订阅者)
```

#### 关键设计

1. **SystemId 校验**：UDP 数据包头部包含 `SystemId`，客户端通过校验该值识别是否是同一服务端的数据包

```csharp
// 客户端接收时检查
if (ServerId != SystemId)
{
    Logger.Warn($"{ServerMark} Udp组播数据SystemId不匹配，已忽略！");
    continue;
}
```

2. **Loopback 模式**：特殊处理本机回环场景

```csharp
if (UdpSocketServer.LoopbackIP == ServerIP)
{
    _client.JoinMulticastGroup(
        IPAddress.Parse(UdpSocketServer.LoopbackSubIP),  // 239.0.0.1
        IPAddress.Parse(localIp));
}
```

3. **广播支持**：启用 `Broadcast` 和 `ReuseAddress` socket 选项

```csharp
_client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
_client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
```

### 2.4 Channel 异步消息队列

库大量使用 `System.Threading.Channels` 实现高效异步消息传递：

```csharp
// TCP 服务端请求队列（包含客户端键和命令）
private readonly Channel<(string ClientKey, SocketCommand Command)> _requests =
    Channel.CreateUnbounded<(string, SocketCommand)>();

// TCP 客户端响应队列
private readonly Channel<SocketCommand> _responses = Channel.CreateUnbounded<SocketCommand>();

// UDP 客户端接收缓冲区
private readonly Channel<SocketCommand> _receivedBuffers = Channel.CreateUnbounded<SocketCommand>();

// 生产者
await _requests.Writer.WriteAsync((tcpClientKey, new SocketCommand(headInfo, buffer, socket)));

// 消费者
await foreach (var (clientKey, command) in _requests.Reader.ReadAllAsync())
{
    await EventBus.EventBus.Default.PublishAsync(command);
}
```

**优势：**
- 无锁并发：比 `BlockingCollection` 更高效
- 异步友好：天然支持 `async/await`
- 内存高效：可配置 bounded/full 模式
- 消除轮询：`ReadAllAsync()` 有数据时自动唤醒，无数据时休眠，CPU 占用低

### 2.5 事件总线集成

`TcpSocketClient` 和 `TcpSocketServer` 通过 `EventBus` 事件总线发布消息：

```csharp
// 发布 TCP 客户端错误事件
await EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(this, msg));

// 发布 Socket 命令
await EventBus.EventBus.Default.PublishAsync(command);
```

用户可以通过订阅事件总线获取网络消息，实现解耦：

```csharp
EventBus.Default.Subscribe<SocketCommand>(async (sender, cmd) =>
{
    if (cmd.IsCommand<Heartbeat>())
    {
        var heartbeat = cmd.GetCommand<Heartbeat>();
        // 处理心跳...
    }
});
```

---

## 三、关键设计模式与最佳实践

### 3.1 责任链模式

数据从 Socket 读取到最终交付给用户，经过多个处理环节：

```
Socket 原始字节
    │
    ├─► ReadPacketAsync      // 读取完整数据包
    │
    ├─► ReadHead             // 解析头部
    │
    ├─► Deserialize          // 反序列化为对象
    │
    └─► EventBus.Publish     // 事件发布
```

### 3.2 生产者-消费者模式

```
生产者 (Socket 接收线程)          消费者 (EventBus 订阅者)
      │                                │
      ├─► Channel.Writer.WriteAsync ───┼─► Channel.Reader.ReadAllAsync
      │                                │
      └─► ...                          └─► ...
```

### 3.3 高性能设计

| 技术 | 应用场景 | 收益 |
|------|----------|------|
| `ArrayPool<byte>.Shared` | Socket 缓冲区管理 | 减少 GC 压力 |
| `Span<T>/Memory<T>` | 字节数组操作 | 避免内存拷贝 |
| `Channel<T>` | 异步消息队列 | 无锁并发 |
| `ConcurrentDictionary` | 客户端会话缓存 | 线程安全访问 |
| `PeriodicTimer` | 定时检测任务 | 高效定时 |

### 3.4 错误处理策略

```csharp
try
{
    // 业务逻辑
}
catch (SocketException ex)
{
    // 区分可恢复和不可恢复错误
    if (ex.SocketErrorCode == SocketError.Interrupted)
    {
        // 中断，优雅退出
    }
    else
    {
        // 记录错误并通知
        await EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(...));
    }
}
catch (Exception ex)
{
    Logger.Error($"接收Udp数据异常：{ex.Message}");
}
```

---

## 四、使用示例

### 4.1 定义网络消息

```csharp
// 登录请求
[NetHead(id: 1, version: 1)]
public class LoginRequest : INetObject
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

// 登录响应
[NetHead(id: 2, version: 1)]
public class LoginResponse : INetObject
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
```

### 4.2 TCP 服务器使用

```csharp
var server = new TcpSocketServer();

// 订阅消息
EventBus.Default.Subscribe<SocketCommand>(async (sender, cmd) =>
{
    if (cmd.IsCommand<LoginRequest>())
    {
        var request = cmd.GetCommand<LoginRequest>();
        Console.WriteLine($"收到登录请求: {request.Username}");

        var response = new LoginResponse { Success = true, Message = "登录成功" };
        // 发送响应...
    }
});

await server.StartAsync("GameServer", "0.0.0.0", 8888);
```

### 4.3 TCP 客户端使用

```csharp
var client = new TcpSocketClient();
await client.ConnectAsync("GameServer", "127.0.0.1", 8888);

// 发送消息
var request = new LoginRequest { Username = "user1", Password = "pass" };
await client.SendCommandAsync(request);

// 订阅响应
EventBus.Default.Subscribe<SocketCommand>(async (sender, cmd) =>
{
    if (cmd.IsCommand<LoginResponse>())
    {
        var response = cmd.GetCommand<LoginResponse>();
        Console.WriteLine($"登录结果: {response.Success}");
    }
});
```

### 4.4 UDP 组播使用

```csharp
// 服务端
var udpServer = new UdpSocketServer();
udpServer.Start("BroadcastServer", 1234567890, "239.0.0.1", 8888);

// 发送广播
var heartbeat = new Heartbeat { TaskId = 1 };
await udpServer.SendCommandAsync(heartbeat, DateTimeOffset.UtcNow);

// 客户端
var udpClient = new UdpSocketClient();
await udpClient.ConnectAsync("BroadcastClient", "239.0.0.1", 8888, "0.0.0.0", 1234567890);

udpClient.Received += (sender, cmd) =>
{
    if (cmd.IsCommand<Heartbeat>())
    {
        var heartbeat = cmd.GetCommand<Heartbeat>();
        Console.WriteLine($"收到心跳: {heartbeat.TaskId}");
    }
};
```

---

## 五、依赖关系

```
CodeWF.NetWrapper
    │
    ├── CodeWF.NetWeaver        # 核心序列化库
    │       └── (无外部依赖)
    │
    ├── CodeWF.EventBus         # 事件总线
    │
    └── CodeWF.Log.Core         # 日志框架
```

---

## 六、总结

CodeWF.NetWeaver 是一个设计精良的网络通信库，其核心特点包括：

1. **简洁的协议设计**：固定头部 + 可扩展 body 的二进制协议
2. **高性能实现**：Span<T>、ArrayPool、Channel 等现代 C# 特性的应用
3. **完善的抽象**：通过 INetObject 接口和特性系统实现灵活的消息定义
4. **清晰的架构**：分层设计，职责明确，易于扩展和维护
5. **丰富的内置消息**：心跳检测、通用响应等常用功能开箱即用

通过本文档，您应该能够理解库的设计原理，并能够基于此库快速开发 TCP/UDP 网络通信应用。
