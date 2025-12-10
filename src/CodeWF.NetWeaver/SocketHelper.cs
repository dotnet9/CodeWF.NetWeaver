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
            var lenBuffer = new byte[4];
            var bytesRead = await socket.ReceiveAsync(new ArraySegment<byte>(lenBuffer), SocketFlags.None, cancellationToken);
            if (bytesRead != 4)
            {
                return (false, Array.Empty<byte>(), default);
            }

            var bufferLen = BitConverter.ToInt32(lenBuffer, 0);
            var buffer = new byte[bufferLen];

            Array.Copy(lenBuffer, buffer, 4);
            var totalBytesRead = 4;

            while (totalBytesRead < bufferLen)
            {
                bytesRead = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, totalBytesRead, bufferLen - totalBytesRead), SocketFlags.None, cancellationToken);
                if (bytesRead == 0)
                {
                    return (false, Array.Empty<byte>(), default);
                }
                totalBytesRead += bytesRead;
            }

            var readIndex = 0;
            var success = ReadHead(buffer, ref readIndex, out var netObject);
            return (success, buffer, netObject);
        }
    }
}