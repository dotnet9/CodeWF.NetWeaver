using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver.Tests.Models
{
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
}