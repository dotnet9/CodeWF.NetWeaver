using CodeWF.NetWeaver;
using CodeWF.Tools.Extensions;
using SocketDto.Enums;
using SocketDto.Response;
using SocketDto.Udp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace SocketTest.Server.Features.Processes.Services;

internal sealed class ProcessSnapshotProvider : IProcessSnapshotProvider
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, ProcessHistory> _previousHistory = [];
    private readonly IProcessMetricsSampler _metricsSampler;
    private ProcessSnapshotState _snapshotState;
    private DateTime _lastCaptureUtc = DateTime.UtcNow.AddSeconds(-1);

    public ProcessSnapshotProvider(IProcessMetricsSampler metricsSampler)
    {
        _metricsSampler = metricsSampler ?? throw new ArgumentNullException(nameof(metricsSampler));
        _snapshotState = new ProcessSnapshotState(
            Array.Empty<ProcessItem>(),
            Array.Empty<int>(),
            BuildServiceInfo(DateTime.Now.GetSpecialUnixTimeSeconds(TimestampStartYear)));
    }

    public const int TimestampStartYear = 2023;

    public ProcessSnapshotRefreshResult RefreshSnapshot()
    {
        lock (_syncRoot)
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = DateTime.Now;
            var elapsedSeconds = Math.Max((nowUtc - _lastCaptureUtc).TotalSeconds, 0.25d);
            var currentTimestamp = nowLocal.GetSpecialUnixTimeSeconds(TimestampStartYear);
            var processSnapshots = new List<ProcessSnapshotItem>();
            var nextHistory = new Dictionary<int, ProcessHistory>();
            var networkConnections = _metricsSampler.GetActiveConnectionCounts();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (!TryCreateProcessItem(
                            process,
                            elapsedSeconds,
                            currentTimestamp,
                            networkConnections,
                            out var snapshotItem,
                            out var history))
                    {
                        continue;
                    }

                    processSnapshots.Add(snapshotItem);
                    nextHistory[snapshotItem.Item.Pid] = history;
                }
            }

            ApplyActivityScores(processSnapshots);

            var processItems = processSnapshots
                .Select(snapshot => snapshot.Item)
                .OrderBy(item => item.Pid)
                .ToArray();

            var newProcessIds = processItems.Select(item => item.Pid).ToArray();
            var previousProcessIds = Volatile.Read(ref _snapshotState).ProcessIds;
            var previousProcessIdSet = previousProcessIds.ToHashSet();
            var currentProcessIdSet = newProcessIds.ToHashSet();
            var addedProcessCount = currentProcessIdSet.Except(previousProcessIdSet).Count();
            var removedProcessCount = previousProcessIdSet.Except(currentProcessIdSet).Count();
            var countChanged = previousProcessIds.Length != newProcessIds.Length;
            var structureChanged = countChanged || addedProcessCount > 0 || removedProcessCount > 0;

            _previousHistory.Clear();
            foreach (var pair in nextHistory)
            {
                _previousHistory[pair.Key] = pair.Value;
            }

            Volatile.Write(
                ref _snapshotState,
                new ProcessSnapshotState(
                    processItems,
                    newProcessIds,
                    BuildServiceInfo(currentTimestamp)));
            _lastCaptureUtc = nowUtc;

            return new ProcessSnapshotRefreshResult(
                structureChanged,
                processItems.Length,
                addedProcessCount,
                removedProcessCount);
        }
    }

    public bool IsInitialized
    {
        get
        {
            var snapshotState = Volatile.Read(ref _snapshotState);
            return snapshotState.ProcessIds.Length > 0 || snapshotState.Processes.Length > 0;
        }
    }

    public int ProcessCount
    {
        get
        {
            return Volatile.Read(ref _snapshotState).Processes.Length;
        }
    }

    public ResponseServiceInfo GetServiceInfo(int taskId)
    {
        var serviceInfo = Volatile.Read(ref _snapshotState).ServiceInfo;
        return new ResponseServiceInfo
        {
            TaskId = taskId,
            OS = serviceInfo.OS,
            MemorySize = serviceInfo.MemorySize,
            ProcessorCount = serviceInfo.ProcessorCount,
            DiskSize = serviceInfo.DiskSize,
            NetworkBandwidth = serviceInfo.NetworkBandwidth,
            Ips = serviceInfo.Ips,
            TimestampStartYear = serviceInfo.TimestampStartYear,
            LastUpdateTime = serviceInfo.LastUpdateTime
        };
    }

    public int[] GetProcessIds()
    {
        return Volatile.Read(ref _snapshotState).ProcessIds.ToArray();
    }

    public List<ProcessItem> GetProcessPage(int pageSize, int pageIndex)
    {
        var processes = Volatile.Read(ref _snapshotState).Processes;
        return processes
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(CloneProcessItem)
            .ToList();
    }

    public List<ProcessItem> GetAllProcesses()
    {
        return Volatile.Read(ref _snapshotState).Processes.Select(CloneProcessItem).ToList();
    }

    public void CalculateRealtimeUdpPage(int packetSize, out int pageSize, out int pageCount)
    {
        var totalCount = Volatile.Read(ref _snapshotState).Processes.Length;
        pageSize = (packetSize - SerializeHelper.PacketHeadLen - sizeof(int) * 8) / (sizeof(short) * 4);
        pageSize = Math.Max(pageSize, 1);
        pageCount = GetPageCount(totalCount, pageSize);
    }

    public void CalculateGeneralUdpPage(int packetSize, out int pageSize, out int pageCount)
    {
        var totalCount = Volatile.Read(ref _snapshotState).Processes.Length;
        pageSize = (packetSize - SerializeHelper.PacketHeadLen - sizeof(int) * 11) /
                   (sizeof(byte) * 5 + sizeof(short) + sizeof(uint));
        pageSize = Math.Max(pageSize, 1);
        pageCount = GetPageCount(totalCount, pageSize);
    }

    public UpdateRealtimeProcessList BuildRealtimeUpdatePage(int pageSize, int pageIndex)
    {
        var processes = Volatile.Read(ref _snapshotState).Processes;
        var page = processes
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToArray();

        return new UpdateRealtimeProcessList
        {
            TotalSize = processes.Length,
            PageSize = pageSize,
            PageCount = GetPageCount(processes.Length, pageSize),
            PageIndex = pageIndex,
            Cpus = ToByteArray(page.Select(item => item.Cpu)),
            Memories = ToByteArray(page.Select(item => item.Memory)),
            Disks = ToByteArray(page.Select(item => item.Disk)),
            Networks = ToByteArray(page.Select(item => item.Network))
        };
    }

    public UpdateGeneralProcessList BuildGeneralUpdatePage(int pageSize, int pageIndex)
    {
        var processes = Volatile.Read(ref _snapshotState).Processes;
        var page = processes
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToArray();

        return new UpdateGeneralProcessList
        {
            TotalSize = processes.Length,
            PageSize = pageSize,
            PageCount = GetPageCount(processes.Length, pageSize),
            PageIndex = pageIndex,
            ProcessStatuses = page.Select(item => item.ProcessStatus).ToArray(),
            AlarmStatuses = page.Select(item => item.AlarmStatus).ToArray(),
            Gpus = ToByteArray(page.Select(item => item.Gpu)),
            GpuEngine = page.Select(item => item.GpuEngine).ToArray(),
            PowerUsage = page.Select(item => item.PowerUsage).ToArray(),
            PowerUsageTrend = page.Select(item => item.PowerUsageTrend).ToArray(),
            UpdateTimes = ToByteArray(page.Select(item => item.UpdateTime))
        };
    }

    public bool TryTerminateProcess(int processId, bool killEntireProcessTree, out string message)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                message = $"进程 {processId} 已退出。";
                return false;
            }

            process.Kill(killEntireProcessTree);
            process.WaitForExit(5000);

            message = killEntireProcessTree
                ? $"已结束进程 {processId}，并同步结束其子进程树。"
                : $"已结束进程 {processId}。";
            return true;
        }
        catch (ArgumentException)
        {
            message = $"未找到进程 {processId}。";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public static int GetPageCount(int totalCount, int pageSize)
    {
        if (totalCount <= 0)
        {
            return 1;
        }

        return (totalCount + pageSize - 1) / pageSize;
    }

    private bool TryCreateProcessItem(
        Process process,
        double elapsedSeconds,
        uint currentTimestamp,
        IReadOnlyDictionary<int, int> networkConnections,
        out ProcessSnapshotItem snapshotItem,
        out ProcessHistory history)
    {
        snapshotItem = default!;
        history = default!;

        try
        {
            if (process.HasExited)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        var pid = SafeGet(() => process.Id, -1);
        if (pid <= 0)
        {
            return false;
        }

        var processName = SafeGet(() => process.ProcessName, string.Empty);
        var startTimeUtc = TryGetStartTimeUtc(process);
        var totalProcessorTime = SafeGet(() => process.TotalProcessorTime, TimeSpan.Zero);
        var mainModulePath = TryGetMainModulePath(process);
        var publisher = TryGetPublisher(mainModulePath);
        var previous = _previousHistory.TryGetValue(pid, out var previousHistory) &&
                       previousHistory.StartTimeUtc == startTimeUtc
            ? previousHistory
            : null;
        var hasIoCounters = _metricsSampler.TryGetProcessIoDataBytes(process, out var totalIoBytes);

        var cpuUsage = CalculateCpuUsage(totalProcessorTime, previous?.TotalProcessorTime, elapsedSeconds);
        var memoryUsage = CalculateMemoryUsage(SafeGet(() => process.WorkingSet64, 0L));
        var powerUsage = CalculatePowerUsage(cpuUsage);
        var powerTrend = CalculatePowerTrend(previous?.CpuUsage ?? cpuUsage, cpuUsage);
        var processType = DetermineProcessType(process);
        var processStatus = DetermineProcessStatus(process);
        var alarmStatus = DetermineAlarmStatus(cpuUsage, memoryUsage);
        var updateTime = currentTimestamp;
        var lastUpdateTime = previous?.UpdateTime ?? currentTimestamp;

        var item = new ProcessItem
        {
            Pid = pid,
            Name = string.IsNullOrWhiteSpace(processName) ? $"Process-{pid}" : processName,
            Type = (byte)processType,
            ProcessStatus = (byte)processStatus,
            AlarmStatus = (byte)alarmStatus,
            Publisher = publisher,
            CommandLine = mainModulePath,
            Cpu = cpuUsage,
            Memory = memoryUsage,
            Disk = 0,
            Network = 0,
            Gpu = 0,
            GpuEngine = (byte)GpuEngine.None,
            PowerUsage = (byte)powerUsage,
            PowerUsageTrend = (byte)powerTrend,
            LastUpdateTime = lastUpdateTime,
            UpdateTime = updateTime
        };

        var diskBytesPerSecond = previous != null && hasIoCounters
            ? CalculateIoBytesPerSecond(previous.IoDataBytes, totalIoBytes, elapsedSeconds)
            : 0d;
        var networkActivityCount = networkConnections.TryGetValue(pid, out var activeConnections) ? activeConnections : 0;

        snapshotItem = new ProcessSnapshotItem(item, diskBytesPerSecond, networkActivityCount);
        history = new ProcessHistory(
            startTimeUtc,
            totalProcessorTime,
            cpuUsage,
            updateTime,
            hasIoCounters ? totalIoBytes : 0);
        return true;
    }

    private static void ApplyActivityScores(IReadOnlyList<ProcessSnapshotItem> snapshots)
    {
        var maxDiskBytesPerSecond = snapshots.Count == 0
            ? 0d
            : snapshots.Max(snapshot => snapshot.DiskBytesPerSecond);
        var maxNetworkConnections = snapshots.Count == 0
            ? 0
            : snapshots.Max(snapshot => snapshot.NetworkActivityCount);

        foreach (var snapshot in snapshots)
        {
            snapshot.Item.Disk = ToRelativeUsage(snapshot.DiskBytesPerSecond, maxDiskBytesPerSecond);
            snapshot.Item.Network = ToRelativeUsage(snapshot.NetworkActivityCount, maxNetworkConnections);
        }
    }

    private ResponseServiceInfo BuildServiceInfo(uint currentTimestamp)
    {
        var totalMemoryBytes = GetTotalPhysicalMemoryBytes();
        var totalDiskBytes = DriveInfo.GetDrives()
            .Where(drive => SafeGet(() => drive.IsReady, false))
            .Sum(drive => SafeGet(() => drive.TotalSize, 0L));
        var totalBandwidthMbps = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Select(network => network.Speed)
            .Where(speed => speed > 0)
            .DefaultIfEmpty(0)
            .Sum(speed => speed / 1024d / 1024d);

        return new ResponseServiceInfo
        {
            OS = RuntimeInformation.OSDescription,
            MemorySize = ToByteGb(totalMemoryBytes),
            ProcessorCount = (byte)Math.Min(Environment.ProcessorCount, byte.MaxValue),
            DiskSize = ToShortGb(totalDiskBytes),
            NetworkBandwidth = (short)Math.Clamp((int)Math.Round(totalBandwidthMbps), 0, short.MaxValue),
            Ips = string.Join(";",
                Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
            TimestampStartYear = TimestampStartYear,
            LastUpdateTime = currentTimestamp
        };
    }

    private static byte ToByteGb(long bytes)
    {
        if (bytes <= 0)
        {
            return 0;
        }

        return (byte)Math.Clamp((int)Math.Round(bytes / 1024d / 1024d / 1024d), 0, byte.MaxValue);
    }

    private static short ToShortGb(long bytes)
    {
        if (bytes <= 0)
        {
            return 0;
        }

        return (short)Math.Clamp((int)Math.Round(bytes / 1024d / 1024d / 1024d), 0, short.MaxValue);
    }

    private static long GetTotalPhysicalMemoryBytes()
    {
        try
        {
            var computerInfoType = Type.GetType("Microsoft.VisualBasic.Devices.ComputerInfo, Microsoft.VisualBasic");
            if (computerInfoType != null)
            {
                var instance = Activator.CreateInstance(computerInfoType);
                var property = computerInfoType.GetProperty("TotalPhysicalMemory");
                if (instance != null && property?.GetValue(instance) is ulong totalPhysicalMemory)
                {
                    return (long)Math.Min(totalPhysicalMemory, (ulong)long.MaxValue);
                }
            }
        }
        catch
        {
        }

        var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return availableMemory > 0 ? availableMemory : 0;
    }

    private static DateTime? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPublisher(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(filePath).CompanyName;
        }
        catch
        {
            return null;
        }
    }

    private static ProcessType DetermineProcessType(Process process)
    {
        try
        {
            if (process.MainWindowHandle != IntPtr.Zero || !string.IsNullOrWhiteSpace(process.MainWindowTitle))
            {
                return ProcessType.Application;
            }
        }
        catch
        {
        }

        return ProcessType.BackgroundProcess;
    }

    private static ProcessStatus DetermineProcessStatus(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return ProcessStatus.Terminated;
            }

            if (OperatingSystem.IsWindows() && process.MainWindowHandle != IntPtr.Zero && !process.Responding)
            {
                return ProcessStatus.Blocked;
            }
        }
        catch
        {
            return ProcessStatus.Running;
        }

        return ProcessStatus.Running;
    }

    private static AlarmStatus DetermineAlarmStatus(short cpuUsage, short memoryUsage)
    {
        var alarmStatus = AlarmStatus.Normal;
        if (cpuUsage >= 900)
        {
            alarmStatus |= AlarmStatus.Overtime;
        }

        if (memoryUsage >= 900)
        {
            alarmStatus |= AlarmStatus.OverLimit;
        }

        return alarmStatus;
    }

    private static short CalculateCpuUsage(
        TimeSpan totalProcessorTime,
        TimeSpan? previousTotalProcessorTime,
        double elapsedSeconds)
    {
        if (previousTotalProcessorTime == null || elapsedSeconds <= 0)
        {
            return 0;
        }

        var cpuMilliseconds = (totalProcessorTime - previousTotalProcessorTime.Value).TotalMilliseconds;
        if (cpuMilliseconds <= 0)
        {
            return 0;
        }

        var cpuPercent = cpuMilliseconds / (elapsedSeconds * 1000d * Environment.ProcessorCount) * 100d;
        return (short)Math.Clamp((int)Math.Round(cpuPercent * 10d), 0, 1000);
    }

    private static short CalculateMemoryUsage(long workingSetBytes)
    {
        var totalMemoryBytes = GetTotalPhysicalMemoryBytes();
        if (totalMemoryBytes <= 0 || workingSetBytes <= 0)
        {
            return 0;
        }

        var memoryPercent = workingSetBytes / (double)totalMemoryBytes * 100d;
        return (short)Math.Clamp((int)Math.Round(memoryPercent * 10d), 0, 1000);
    }

    private static double CalculateIoBytesPerSecond(ulong previousBytes, ulong currentBytes, double elapsedSeconds)
    {
        if (currentBytes <= previousBytes || elapsedSeconds <= 0)
        {
            return 0d;
        }

        return (currentBytes - previousBytes) / elapsedSeconds;
    }

    private static PowerUsage CalculatePowerUsage(short cpuUsage)
    {
        return cpuUsage switch
        {
            >= 800 => PowerUsage.VeryHigh,
            >= 500 => PowerUsage.High,
            >= 250 => PowerUsage.Moderate,
            >= 100 => PowerUsage.Low,
            _ => PowerUsage.VeryLow
        };
    }

    private static PowerUsage CalculatePowerTrend(short previousCpuUsage, short currentCpuUsage)
    {
        var delta = currentCpuUsage - previousCpuUsage;
        return delta switch
        {
            >= 250 => PowerUsage.VeryHigh,
            >= 120 => PowerUsage.High,
            >= 40 => PowerUsage.Moderate,
            > 0 => PowerUsage.Low,
            _ => PowerUsage.VeryLow
        };
    }

    private static T SafeGet<T>(Func<T> accessor, T fallback)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return fallback;
        }
    }

    private static ProcessItem CloneProcessItem(ProcessItem item) => item with { };

    private static short ToRelativeUsage(double currentValue, double maxValue)
    {
        if (currentValue <= 0 || maxValue <= 0)
        {
            return 0;
        }

        return (short)Math.Clamp((int)Math.Round(currentValue / maxValue * 1000d), 0, 1000);
    }

    private static short ToRelativeUsage(int currentValue, int maxValue)
    {
        if (currentValue <= 0 || maxValue <= 0)
        {
            return 0;
        }

        return (short)Math.Clamp((int)Math.Round(currentValue / (double)maxValue * 1000d), 0, 1000);
    }

    private static byte[] ToByteArray(IEnumerable<short> values)
    {
        var array = values.ToArray();
        var bytes = new byte[array.Length * sizeof(short)];
        Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] ToByteArray(IEnumerable<uint> values)
    {
        var array = values.ToArray();
        var bytes = new byte[array.Length * sizeof(uint)];
        Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private sealed record ProcessHistory(
        DateTime? StartTimeUtc,
        TimeSpan TotalProcessorTime,
        short CpuUsage,
        uint UpdateTime,
        ulong IoDataBytes);

    private sealed class ProcessSnapshotItem
    {
        public ProcessSnapshotItem(ProcessItem item, double diskBytesPerSecond, int networkActivityCount)
        {
            Item = item;
            DiskBytesPerSecond = diskBytesPerSecond;
            NetworkActivityCount = networkActivityCount;
        }

        public ProcessItem Item { get; }

        public double DiskBytesPerSecond { get; }

        public int NetworkActivityCount { get; }
    }

    private sealed record ProcessSnapshotState(
        ProcessItem[] Processes,
        int[] ProcessIds,
        ResponseServiceInfo ServiceInfo);
}

internal readonly record struct ProcessSnapshotRefreshResult(
    bool StructureChanged,
    int ProcessCount,
    int AddedProcessCount,
    int RemovedProcessCount);
