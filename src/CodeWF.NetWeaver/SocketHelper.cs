using System;
using System.Net.Sockets;
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
        /// 从Socket读取数据包
        /// </summary>
        /// <param name="socket">Socket对象</param>
        /// <param name="buffer">输出参数，包含读取的数据包</param>
        /// <param name="netObject">输出参数，包含解析的网络头信息</param>
        /// <returns>是否成功读取数据包</returns>
        public static bool ReadPacket(this Socket socket, out byte[] buffer, out NetHeadInfo netObject)
        {
            var lenBuffer = ReceiveBuffer(socket, 4);
            var bufferLen = BitConverter.ToInt32(lenBuffer, 0);

            var exceptLenBuffer = ReceiveBuffer(socket, bufferLen - 4);

            buffer = new byte[bufferLen];

            Array.Copy(lenBuffer, buffer, 4);
            Buffer.BlockCopy(exceptLenBuffer, 0, buffer, 4, bufferLen - 4);

            var readIndex = 0;
            return ReadHead(buffer, ref readIndex, out netObject);
        }

        /// <summary>
        /// 从Socket接收指定长度的缓冲区数据
        /// </summary>
        /// <param name="client">Socket客户端</param>
        /// <param name="count">要接收的数据长度</param>
        /// <returns>包含接收数据的字节数组</returns>
        private static byte[] ReceiveBuffer(Socket client, int count)
        {
            var buffer = new byte[count];
            var bytesReadAllCount = 0;
            while (bytesReadAllCount != count)
                bytesReadAllCount +=
                    client.Receive(buffer, bytesReadAllCount, count - bytesReadAllCount, SocketFlags.None);

            return buffer;
        }
    }
}