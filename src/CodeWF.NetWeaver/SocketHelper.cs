using System;
using System.Buffers;
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
    /// 网络数据包头部固定大小，头部固定字段数据类型大小(PacketSize + SystemId + ObjectId + ObjectVersion + UnixTimeMilliseconds)
    /// </summary>
    public const int PacketHeadLen = sizeof(int) + sizeof(long) + sizeof(ushort) + sizeof(byte) + sizeof(long);

    /// <summary>
    /// 数组、列表、字典等数据结构数据量字段大小：如Length、Count
    /// </summary>
    public const int ArrayOrDictionaryCountSize = 4;

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
    /// <returns>元组，包含是否成功读取数据包、读取的数据包和解析的网络头信息</returns>
    public static async Task<(bool Success, byte[] Buffer, NetHeadInfo NetObject)> ReadPacketAsync(this Socket socket,
        CancellationToken cancellationToken = default)
    {
        // 使用ArrayPool.Shared.Rent来减少内存分配
        var lenBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            if (!await socket.ReceiveExactAsync(lenBuffer, 0, 4, cancellationToken))
            {
                return (false, Array.Empty<byte>(), new NetHeadInfo());
            }

            var bufferLen = BitConverter.ToInt32(lenBuffer.AsSpan(0, 4));
            var buffer = ArrayPool<byte>.Shared.Rent(bufferLen);
            try
            {
                lenBuffer.AsSpan(0, 4).CopyTo(buffer.AsSpan(0, 4));

                if (!await socket.ReceiveExactAsync(buffer, 4, bufferLen - 4, cancellationToken))
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return (false, Array.Empty<byte>(), new NetHeadInfo());
                }

                var readIndex = 0;
                var success = ReadHead(buffer, ref readIndex, out var netObject);
                return (success, buffer, netObject!);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lenBuffer);
        }
    }
}
