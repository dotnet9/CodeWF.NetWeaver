using System;
using System.Net.Sockets;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver
{
    public static partial class SerializeHelper
    {
        public const int MaxUdpPacketSize = 65507;

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