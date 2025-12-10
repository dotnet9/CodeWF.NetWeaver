using System;
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
            var lenBuffer = await ReceiveBufferAsync(socket, 4, cancellationToken);
            var bufferLen = BitConverter.ToInt32(lenBuffer, 0);

            var exceptLenBuffer = await ReceiveBufferAsync(socket, bufferLen - 4, cancellationToken);

            var buffer = new byte[bufferLen];

            Array.Copy(lenBuffer, buffer, 4);
            Buffer.BlockCopy(exceptLenBuffer, 0, buffer, 4, bufferLen - 4);

            var readIndex = 0;
            var success = ReadHead(buffer, ref readIndex, out var netObject);
            return (success, buffer, netObject);
        }
        
        /// <summary>
        /// 异步从Socket接收指定长度的缓冲区数据
        /// </summary>
        /// <param name="client">Socket客户端</param>
        /// <param name="count">要接收的数据长度</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>包含接收数据的字节数组</returns>
        private static async Task<byte[]> ReceiveBufferAsync(Socket client, int count, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[count];
            var bytesReadAllCount = 0;
            using (var stream = new NetworkStream(client, ownsSocket: false))
            {
                while (bytesReadAllCount != count)
                {
                    var bytesRead = await stream.ReadAsync(buffer, bytesReadAllCount, count - bytesReadAllCount, cancellationToken);
                    bytesReadAllCount += bytesRead;
                }
            }

            return buffer;
        }
    }
}