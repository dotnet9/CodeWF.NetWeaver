using System;

namespace SocketTest.Server.Services;

/// <summary>
/// 进程快照提供器工厂。后续新增平台时只需扩展这里的装配逻辑。
/// </summary>
internal static class ProcessSnapshotProviderFactory
{
    public static IProcessSnapshotProvider CreateDefault()
    {
        IProcessMetricsSampler sampler = OperatingSystem.IsWindows()
            ? new WindowsProcessMetricsSampler()
            : new CrossPlatformProcessMetricsSampler();

        return new ProcessSnapshotProvider(sampler);
    }
}
