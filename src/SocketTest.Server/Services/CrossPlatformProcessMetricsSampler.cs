using System.Collections.Generic;
using System.Diagnostics;

namespace SocketTest.Server.Services;

/// <summary>
/// 默认跨平台采样器。优先保证 Windows/Linux/mac 可运行，细粒度指标后续可按平台增强。
/// </summary>
internal sealed class CrossPlatformProcessMetricsSampler : IProcessMetricsSampler
{
    public string PlatformName => "CrossPlatform";

    public bool TryGetProcessIoDataBytes(Process process, out ulong totalIoBytes)
    {
        totalIoBytes = 0;
        return false;
    }

    public IReadOnlyDictionary<int, int> GetActiveConnectionCounts()
    {
        return new Dictionary<int, int>();
    }
}
