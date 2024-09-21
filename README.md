# CodeWF.NetWeaver

CodeWF.NetWeaver 是一个简洁而强大的C#库，支持AOT，用于处理TCP和UDP数据包的组包和解包操作。

CodeWF.NetWeaver is a concise and powerful C# library that supports AOT for handling TCP and UDP packet grouping and unpacking operations. 

## 安装(Installer)

```bash
NuGet\Install-Package CodeWF.NetWeaver -Version 1.3.0
```

## 定义通信对象(Define net object)

定义数据包如下（来自单元测试`CodeWF.NetWeaver.Tests`），`ResponseProcessList`类继承自`INetObject`, `NetHead`配置数据包标识和版本

```csharp
[NetHead(10, 1)]
public class ResponseProcessList : INetObject
{
    public int TaskId { get; set; }

    public int TotalSize { get; set; }

    public int PageSize { get; set; }

    public int PageCount { get; set; }

    public int PageIndex { get; set; }

    public List<ProcessItem>? Processes { get; set; }
}

public record ProcessItem
{
    public int Pid { get; set; }

    public string? Name { get; set; }

    public byte Type { get; set; }

    public byte ProcessStatus { get; set; }

    public byte AlarmStatus { get; set; }

    public string? Publisher { get; set; }

    public string? CommandLine { get; set; }

    public short Cpu { get; set; }

    public short Memory { get; set; }

    public short Disk { get; set; }

    public short Network { get; set; }

    public short Gpu { get; set; }

    public byte GpuEngine { get; set; }

    public byte PowerUsage { get; set; }

    public byte PowerUsageTrend { get; set; }

    public uint LastUpdateTime { get; set; }

    public uint UpdateTime { get; set; }
}
```

## 使用(Use)

单元测试数据组包与解包：

```csharp
[Fact]
public void Test_SerializeResponseProcessList_Success()
{
    var netObject = new ResponseProcessList
    {
        TaskId = 3,
        TotalSize = 200,
        PageSize = 3,
        PageCount = 67,
        PageIndex = 1,
        Processes = new List<ProcessItem>()
    };
    var processItem = new ProcessItem
    {
        Pid = 1,
        Name = "CodeWF.NetWeaver",
        Type = (byte)ProcessType.Application,
        ProcessStatus = (byte)ProcessStatus.Running,
        Publisher = "沙漠尽头的狼",
        CommandLine = "dotnet CodeWF.com",
        Cpu = 112,
        Memory = 325,
        Disk = 23,
        Network = 593,
        Gpu = 253,
        GpuEngine = (byte)GpuEngine.None,
        PowerUsage = (byte)PowerUsage.Low,
        PowerUsageTrend = (byte)PowerUsage.Low,
        LastUpdateTime = 23,
        UpdateTime = 53
    };
    netObject.Processes.Add(processItem);

    var buffer = netObject.Serialize(32);
    var desObject = buffer.Deserialize<ResponseProcessList>();

    Assert.Equal(netObject.TotalSize, desObject.TotalSize);
    Assert.NotNull(desObject.Processes);
    Assert.Equal(processItem.Cpu, desObject.Processes[0].Cpu);
    Assert.Equal(processItem.LastUpdateTime, desObject.Processes[0].LastUpdateTime);
}
```

## 参考

- https://github.com/dotnet9/CsharpSocketTest
- https://github.com/dotnet9/CodeWF.EventBus.Socket