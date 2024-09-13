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
        /// 网络数据包头部固定大小
        /// </summary>
        public const int PacketHeadLen = 22;

        /// <summary>
        /// 数组、列表、字典等数据结构数据量字段大小：如Length、Count
        /// </summary>
        public const int ArrayOrDictionaryCountSize = 4;

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