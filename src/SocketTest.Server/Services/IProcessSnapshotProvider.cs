using SocketDto.Response;
using SocketDto.Udp;
using System.Collections.Generic;

namespace SocketTest.Server.Services;

/// <summary>
/// 进程快照提供器接口。上层只依赖该接口，便于未来替换为 Android、iOS 或鸿蒙实现。
/// </summary>
internal interface IProcessSnapshotProvider
{
    ProcessSnapshotRefreshResult RefreshSnapshot();

    bool IsInitialized { get; }

    int ProcessCount { get; }

    ResponseServiceInfo GetServiceInfo(int taskId);

    int[] GetProcessIds();

    List<ProcessItem> GetProcessPage(int pageSize, int pageIndex);

    List<ProcessItem> GetAllProcesses();

    void CalculateRealtimeUdpPage(int packetSize, out int pageSize, out int pageCount);

    void CalculateGeneralUdpPage(int packetSize, out int pageSize, out int pageCount);

    UpdateRealtimeProcessList BuildRealtimeUpdatePage(int pageSize, int pageIndex);

    UpdateGeneralProcessList BuildGeneralUpdatePage(int pageSize, int pageIndex);

    bool TryTerminateProcess(int processId, bool killEntireProcessTree, out string message);
}
