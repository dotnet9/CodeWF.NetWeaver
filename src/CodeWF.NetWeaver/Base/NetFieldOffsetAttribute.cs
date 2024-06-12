using System;

namespace CodeWF.NetWeaver.Base
{
    /// <summary>
    ///     字段或属性bit配置
    /// </summary>
    /// <param name="size"></param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NetFieldOffsetAttribute : Attribute
    {
        /// <summary>
        ///     偏移
        /// </summary>
        public byte Offset { get; }

        /// <summary>
        ///     字段或属性bit大小
        /// </summary>
        public byte Size { get; }

        public NetFieldOffsetAttribute(byte offset, byte size)
        {
            Offset = offset;
            Size = size;
        }
    }
}