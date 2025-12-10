using System;
using System.Buffers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver
{
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
        /// 异步从Socket读取数据包
        /// </summary>
        /// <param name="socket">Socket对象</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>元组，包含是否成功读取数据包、读取的数据包和解析的网络头信息</returns>
        public static async Task<(bool Success, byte[] Buffer, NetHeadInfo NetObject)> ReadPacketAsync(this Socket socket, CancellationToken cancellationToken = default)
        {
            // 使用ArrayPool.Shared.Rent来减少内存分配
            var lenBuffer = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                var bytesRead = await socket.ReceiveAsync(new ArraySegment<byte>(lenBuffer, 0, 4), SocketFlags.None, cancellationToken);
                if (bytesRead != 4)
                {
                    return (false, Array.Empty<byte>(), default);
                }

                // 使用Span<T>来避免不必要的内存拷贝
                var bufferLen = BitConverter.ToInt32(lenBuffer.AsSpan(0, 4));
                var buffer = ArrayPool<byte>.Shared.Rent(bufferLen);
                try
                {
                    // 将长度信息复制到结果缓冲的开头
                    lenBuffer.AsSpan(0, 4).CopyTo(buffer.AsSpan(0, 4));
                    var totalBytesRead = 4;

                    while (totalBytesRead < bufferLen)
                    {
                        bytesRead = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, totalBytesRead, bufferLen - totalBytesRead), SocketFlags.None, cancellationToken);
                        if (bytesRead == 0)
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                            return (false, Array.Empty<byte>(), default);
                        }
                        totalBytesRead += bytesRead;
                    }

                    var readIndex = 0;
                    var success = ReadHead(buffer, ref readIndex, out var netObject);
                    return (success, buffer, netObject);
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
}