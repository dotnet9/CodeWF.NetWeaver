using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SocketTest.Server.Services;

/// <summary>
/// Windows 平台增强采样器，提供进程 I/O 与 TCP 连接统计。
/// </summary>
internal sealed class WindowsProcessMetricsSampler : IProcessMetricsSampler
{
    public string PlatformName => "Windows";

    public bool TryGetProcessIoDataBytes(Process process, out ulong totalIoBytes)
    {
        totalIoBytes = 0;
        try
        {
            if (!NativeMethods.GetProcessIoCounters(process.Handle, out var ioCounters))
            {
                return false;
            }

            totalIoBytes = ioCounters.ReadTransferCount + ioCounters.WriteTransferCount;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyDictionary<int, int> GetActiveConnectionCounts()
    {
        var result = new Dictionary<int, int>();
        try
        {
            var bufferSize = 0;
            _ = NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, NativeMethods.AF_INET,
                NativeMethods.TcpTableOwnerPidAll, 0);
            if (bufferSize <= 0)
            {
                return result;
            }

            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                var status = NativeMethods.GetExtendedTcpTable(buffer, ref bufferSize, true, NativeMethods.AF_INET,
                    NativeMethods.TcpTableOwnerPidAll, 0);
                if (status != 0)
                {
                    return result;
                }

                var rowCount = Marshal.ReadInt32(buffer);
                var rowPtr = buffer + sizeof(int);
                var rowSize = Marshal.SizeOf<NativeMethods.MibTcpRowOwnerPid>();

                for (var i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<NativeMethods.MibTcpRowOwnerPid>(rowPtr + i * rowSize);
                    if (row.State is NativeMethods.MibTcpState.DeleteTcb or NativeMethods.MibTcpState.Closed)
                    {
                        continue;
                    }

                    result[row.OwningPid] = result.TryGetValue(row.OwningPid, out var count) ? count + 1 : 1;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    private static class NativeMethods
    {
        public const int AF_INET = 2;
        public const int TcpTableOwnerPidAll = 5;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters lpIoCounters);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            [MarshalAs(UnmanagedType.Bool)] bool sort,
            int ipVersion,
            int tcpTableType,
            uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        public struct MibTcpRowOwnerPid
        {
            public MibTcpState State;
            public uint LocalAddr;
            public uint LocalPort;
            public uint RemoteAddr;
            public uint RemotePort;
            public int OwningPid;
        }

        public enum MibTcpState
        {
            Closed = 1,
            Listen = 2,
            SynSent = 3,
            SynRcvd = 4,
            Established = 5,
            FinWait1 = 6,
            FinWait2 = 7,
            CloseWait = 8,
            Closing = 9,
            LastAck = 10,
            TimeWait = 11,
            DeleteTcb = 12
        }
    }
}
