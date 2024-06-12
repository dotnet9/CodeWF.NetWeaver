using System;

namespace CodeWF.NetWeaver.Base
{
    /// <summary>
    ///     数据包定义
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class NetHeadAttribute : Attribute
    {
        /// <summary>
        ///     对象Id
        /// </summary>
        public byte Id { get; set; }

        /// <summary>
        ///     对象版本号
        /// </summary>
        public byte Version { get; set; }

        public NetHeadAttribute(byte id, byte version)
        {
            Id = id;
            Version = version;
        }
    }
}