using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver;

public static partial class SerializeHelper
{
    /// <summary>
    /// UDP建议最大包大小
    /// </summary>
    public const int MaxUdpPacketSize = 65507;

    /// <summary>
    /// TCP单包最大大小，避免异常或恶意长度字段导致过大内存分配
    /// </summary>
    public static int MaxTcpPacketSize { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// 网络数据包头部固定大小，头部固定字段数据类型大小(PacketSize + SystemId + ObjectId + ObjectVersion + UnixTimeMilliseconds)
    /// </summary>
    public const int PacketHeadLen = sizeof(int) + sizeof(long) + sizeof(ushort) + sizeof(byte) + sizeof(long);

    /// <summary>
    /// 从Socket异步读取指定数量的字节，确保读取到完整的字节数
    /// </summary>
    /// <param name="socket">Socket对象</param>
    /// <param name="buffer">目标缓冲区</param>
    /// <param name="offset">缓冲区起始偏移</param>
    /// <param name="count">需要读取的字节数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功读取到指定数量的字节</returns>
    public static async Task<bool> ReceiveExactAsync(this Socket socket, byte[] buffer, int offset, int count,
        CancellationToken cancellationToken = default)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < count)
        {
            var bytesRead = await socket.ReceiveAsync(
                new ArraySegment<byte>(buffer, offset + totalBytesRead, count - totalBytesRead), SocketFlags.None,
                cancellationToken);
            if (bytesRead == 0)
            {
                return false;
            }

            totalBytesRead += bytesRead;
        }

        return true;
    }

    /// <summary>
    /// 异步从Socket读取数据包</param>
    /// </summary>
    /// <param name="socket">Socket对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>元组，包含是否成功读取数据包、读取的数据包和解析的网络头信息；失败时数据包和网络头信息不可用</returns>
    public static async Task<(bool Success, byte[]? Buffer, NetHeadInfo? NetObject)> ReadPacketAsync(this Socket socket,
        CancellationToken cancellationToken = default)
    {
        var lenBuffer = new byte[sizeof(int)];
        if (!await socket.ReceiveExactAsync(lenBuffer, 0, lenBuffer.Length, cancellationToken))
        {
            return ReadPacketFailed();
        }

        var bufferLen = BitConverter.ToInt32(lenBuffer.AsSpan(0, lenBuffer.Length));
        if (!IsValidTcpPacketLength(bufferLen))
        {
            return ReadPacketFailed();
        }

        var buffer = new byte[bufferLen];
        lenBuffer.CopyTo(buffer.AsSpan(0, lenBuffer.Length));
        if (!await socket.ReceiveExactAsync(buffer, lenBuffer.Length, bufferLen - lenBuffer.Length,
                cancellationToken))
        {
            return ReadPacketFailed();
        }

        var readIndex = 0;
        var success = ReadHead(buffer, ref readIndex, out var netObject);
        return success
            ? (true, buffer, netObject)
            : ReadPacketFailed();
    }

    private static bool IsValidTcpPacketLength(int bufferLen)
    {
        var maxPacketSize = Math.Max(MaxTcpPacketSize, PacketHeadLen);
        return bufferLen >= PacketHeadLen && bufferLen <= maxPacketSize;
    }

    private static (bool Success, byte[]? Buffer, NetHeadInfo? NetObject) ReadPacketFailed()
    {
        return (false, null, null);
    }
}
