using System.Collections.Generic;
using System.Diagnostics;

namespace SocketTest.Server.Features.Processes.Services;

/// <summary>
/// 进程活动采样器接口。不同平台可提供不同粒度的数据源。
/// </summary>
internal interface IProcessMetricsSampler
{
    /// <summary>
    /// 平台名称，仅用于日志或诊断。
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// 尝试读取进程累计 I/O 字节数。
    /// </summary>
    bool TryGetProcessIoDataBytes(Process process, out ulong totalIoBytes);

    /// <summary>
    /// 获取各进程当前活动 TCP 连接数。
    /// </summary>
    IReadOnlyDictionary<int, int> GetActiveConnectionCounts();
}
