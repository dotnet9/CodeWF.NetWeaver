using System;

namespace CodeWF.NetWeaver.Base
{
    /// <summary>
    /// 网络字段偏移量属性，用于指定网络传输中字段的偏移量和大小
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NetFieldOffsetAttribute : Attribute
    {
        /// <summary>
        /// 获取字段在网络数据包中的偏移量
        /// </summary>
        public byte Offset { get; }

        /// <summary>
        /// 获取字段在网络数据包中的大小（位）
        /// </summary>
        public byte Size { get; }

        /// <summary>
        /// 初始化 NetFieldOffsetAttribute 类的新实例
        /// </summary>
        /// <param name="offset">字段在网络数据包中的偏移量</param>
        /// <param name="size">字段在网络数据包中的大小（位）</param>
        public NetFieldOffsetAttribute(byte offset, byte size)
        {
            Offset = offset;
            Size = size;
        }
    }
}