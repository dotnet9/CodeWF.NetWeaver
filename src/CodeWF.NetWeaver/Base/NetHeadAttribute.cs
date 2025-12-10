using System;

namespace CodeWF.NetWeaver.Base
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class NetHeadAttribute : Attribute
    {
        public ushort Id { get; set; }

        public byte Version { get; set; }

        public NetHeadAttribute(ushort id, byte version)
        {
            Id = id;
            Version = version;
        }
    }
}