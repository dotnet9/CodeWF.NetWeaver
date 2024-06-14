using System;

namespace CodeWF.NetWeaver.Base
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NetFieldOffsetAttribute : Attribute
    {
        public byte Offset { get; }
        public byte Size { get; }

        public NetFieldOffsetAttribute(byte offset, byte size)
        {
            Offset = offset;
            Size = size;
        }
    }
}