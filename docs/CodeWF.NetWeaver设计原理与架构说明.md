# CodeWF.NetWeaver 设计原理与架构说明

本文档说明仓库的内部设计、协议边界和实现取舍，面向维护者和需要扩展协议/封装层的开发者。快速安装、基础用法和项目入口请看根目录 `README.md`。

## 文档职责

- `README.md`：项目入口文档，介绍仓库组成、安装方式、基础使用、构建测试命令和常用 API。
- `docs/CodeWF.NetWeaver设计原理与架构说明.md`：设计文档，解释二进制协议、序列化机制、Socket 封装、文件管理与文件传输的实现结构。

## 总体分层

| 项目 | 职责 |
| --- | --- |
| `CodeWF.NetWeaver` | 核心二进制序列化库，负责数据包头、对象体、数组、集合、字典、枚举和位字段的编码/解码。 |
| `CodeWF.NetWrapper` | 基于 `CodeWF.NetWeaver` 的 TCP/UDP 封装层，负责连接、命令分发、文件系统管理、上传/下载和传输状态事件。 |
| `SocketDto` | 示例协议对象和对象 ID 常量。 |
| `SocketTest.Client` / `SocketTest.Server` | Avalonia 示例程序，用于验证进程监控、远程文件管理和文件传输链路。 |

核心原则是：`CodeWF.NetWeaver` 只关心“对象如何变成网络字节包”，`CodeWF.NetWrapper` 才关心“字节包如何在 Socket、命令、文件和 UI 之间流动”。

## CodeWF.NetWeaver

### 数据包结构

网络包由固定头部和对象正文组成：

```text
Header
- BufferLen: int
- SystemId: long
- ObjectId: ushort
- ObjectVersion: byte
- UnixTimeMilliseconds: long

Body
- SerializeHelper 写出的对象正文
```

头部长度为 23 字节。`Serialize<T>(systemId, sendTime)` 会从 DTO 类型上的 `NetHeadAttribute` 读取 `ObjectId` 和 `Version`，写入头部后追加正文。`Deserialize<T>()` 默认跳过 23 字节头部，从正文开始还原对象。

### DTO 标记

| 类型 | 作用 |
| --- | --- |
| `INetObject` | 标识可作为网络命令发送的对象。 |
| `NetHeadAttribute` | 标记对象 ID 与协议版本。 |
| `NetIgnoreMemberAttribute` | 序列化/反序列化时忽略成员。 |
| `NetFieldOffsetAttribute` | 位字段序列化时声明偏移和长度。 |
| `NetHeadInfo` | 运行时解析出的包头信息。 |

示例：

```csharp
using CodeWF.NetWeaver.Base;

[NetHead(10, 1)]
public sealed class ResponseProcessList : INetObject
{
    public int TaskId { get; set; }

    public int TotalSize { get; set; }

    public List<ProcessItem>? Processes { get; set; }
}
```

### 序列化机制

`SerializeHelper` 按属性声明顺序写入字段，并缓存 `PropertyInfo[]` 降低反射开销。当前支持：

- 标量：`bool`、整数、浮点、`decimal`、`char`、`string`、`enum`
- 数组：写入长度后逐项写入
- 集合：`List<T>`、`IList<T>`、`ICollection<T>`、`IEnumerable<T>`、`IReadOnlyList<T>`、`IReadOnlyCollection<T>`
- 字典：`Dictionary<TKey,TValue>`、`IDictionary<TKey,TValue>`、`IReadOnlyDictionary<TKey,TValue>`
- 嵌套对象：递归写入其公开属性

集合反序列化时优先创建目标具体类型；如果属性声明为接口或抽象类型，则回退到 `List<T>` 或 `Dictionary<TKey,TValue>`。

### 位字段支持

`SerializeHelper.BitField.cs` 提供位级打包/解包能力，适用于紧凑协议字段：

```csharp
public sealed class Flags
{
    [NetFieldOffset(0, 4)]
    public byte Type { get; set; }

    [NetFieldOffset(4, 1)]
    public byte IsValid { get; set; }
}

var buffer = new Flags { Type = 5, IsValid = 1 }.FieldObjectBuffer();
var flags = buffer.ToFieldObject<Flags>();
```

## CodeWF.NetWrapper

### 通信模型

`CodeWF.NetWrapper` 使用 `System.Threading.Channels` 解耦 Socket 接收和业务处理：

- TCP 服务端维护 `ConcurrentDictionary<string, TcpSession>`，按客户端地址管理会话。
- TCP 客户端和服务端收到完整包后解析 `NetHeadInfo`，包装为 `SocketCommand`。
- 非内置文件命令通过 `CodeWF.EventBus` 发布，应用层可自行订阅处理。
- 文件系统和文件传输命令进入独立 Channel，由 Wrapper 内部自动处理。

### TCP 与 UDP

TCP 负责可靠命令、请求响应和文件传输；UDP 负责组播/广播类更新。UDP 包依靠头部 `SystemId` 过滤不同服务端来源，避免客户端误处理其他实例的数据。

## 文件系统管理

服务端文件能力通过 `IManagedFileSystem` 抽象：

```csharp
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
    Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share);
}
```

默认实现是物理文件系统，后续可以替换成移动端容器、沙箱目录或虚拟文件系统。最新代码中服务端路径解析要求请求路径是绝对路径；空路径只在浏览场景下有特殊含义，用于返回磁盘列表或根级信息。

文件管理协议对象包括：

| 类型 | 职责 |
| --- | --- |
| `BrowseFileSystemRequest` / `BrowseFileSystemResponse` | 浏览目录，响应分页返回条目。 |
| `DriveListResponse` / `DiskInfo` | 返回可用磁盘信息。 |
| `CreateDirectoryRequest` / `CreateDirectoryResponse` | 创建目录。 |
| `DeletePathRequest` / `DeletePathResponse` | 删除文件或目录。 |
| `FileTransferReject` | 返回访问拒绝、路径错误、文件状态冲突等失败原因。 |

## 文件传输

文件传输基于 `TaskId + RemoteFilePath` 关联一次会话。客户端和服务端分别维护上传/下载上下文，所有请求、响应、分块、确认和拒绝消息都携带同一个 `TaskId`。

### 协议对象

| 类型 | 职责 |
| --- | --- |
| `FileUploadRequest` / `FileUploadResponse` | 发起上传并确认续传偏移。 |
| `FileDownloadRequest` / `FileDownloadResponse` | 发起下载并确认文件大小、哈希和续传偏移。 |
| `FileChunkData` | 传输文件分块，包含 `BlockIndex`、`Offset`、`BlockSize`、`Data`、`RemoteFilePath`。 |
| `FileChunkAck` | 确认分块写入结果和下一次偏移。 |
| `FileTransferReject` | 拒绝或取消传输。 |
| `FileTransferComplete` | 保留的完成消息类型。当前主流程主要依靠分块确认、上下文移除和结果事件完成闭环。 |

### 行为规则

- 分块大小为 64 KB。
- 上传和下载都支持从已存在文件长度或 `.progress` 记录继续。
- 完成后使用 SHA-256 校验文件一致性。
- 客户端暴露 `FileTransferProgress` 和 `FileTransferOutcome`。
- 服务端暴露 `FileTransferProgress`。
- 取消传输时会发送 `FileTransferReject`，并通过结果事件通知调用方。

上传主流程：

```text
Client -> FileUploadRequest
Server -> FileUploadResponse(AlreadyTransferredBytes)
Client -> FileChunkData(offset)
Server -> FileChunkAck(next offset)
... repeat ...
Server -> hash check
Client -> FileTransferOutcome(success)
```

下载主流程：

```text
Client -> FileDownloadRequest(AlreadyTransferredBytes, local hash)
Server -> FileDownloadResponse(file size, hash, offset)
Server -> FileChunkData(offset)
Client -> FileChunkAck(next offset)
... repeat ...
Client -> hash check + FileTransferOutcome(success)
```

## 对象 ID 管理

`SocketConstants` 管理 Wrapper 内置对象 ID。当前文件相关对象集中在 193-211：

```text
193 FileUploadRequest
194 FileUploadResponse
195 FileChunkData
196 FileChunkAck
197 FileTransferComplete
198 CommonSocketResponse
199 Heartbeat
200 BrowseFileSystemRequest
201 DirectoryEntry
202 CreateDirectoryRequest
203 CreateDirectoryResponse
204 DeletePathRequest
205 DeletePathResponse
206 FileTransferReject
207 FileDownloadRequest
208 FileDownloadResponse
209 BrowseFileSystemResponse
210 DiskInfo
211 DriveListResponse
```

业务 DTO 应继续集中管理对象 ID，避免在多个 DTO 中散落魔法数字。

## 维护建议

- 变更 DTO 字段顺序会影响二进制兼容性，应通过 `ObjectVersion` 管理协议升级。
- 新增 Wrapper 内置命令时同步更新 `SocketConstants`、请求/响应 DTO、分发逻辑和测试。
- 文件能力优先扩展 `IManagedFileSystem`，不要把物理路径假设写死到传输流程里。
- README 保持面向使用者，设计文档保持面向维护者；示例代码以当前公开 API 为准。
