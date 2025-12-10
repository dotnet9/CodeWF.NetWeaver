using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver
{
    /// <summary>
    /// 序列化辅助类，提供对象的序列化和反序列化功能
    /// </summary>
    public partial class SerializeHelper
    {
        /// <summary>
        /// 缓存对象属性信息的字典，提高反射效率
        /// </summary>
        private static readonly ConcurrentDictionary<string, List<PropertyInfo>> ObjectPropertyInfos = new();

        /// <summary>
        /// 复杂类型名称列表，用于识别需要特殊处理的集合类型
        /// </summary>
        private static readonly List<string> ComplexTypeNames = new List<string>()
        {
            typeof(List<>).Name,
            typeof(Dictionary<,>).Name
        };

        /// <summary>
        /// 默认编码，用于字符串的序列化和反序列化
        /// </summary>
        public static Encoding DefaultEncoding = Encoding.UTF8;

        /// <summary>
        /// 获取指定类型的属性信息列表，使用缓存提高效率
        /// </summary>
        /// <param name="type">要获取属性的类型</param>
        /// <returns>属性信息列表</returns>
        private static List<PropertyInfo> GetProperties(Type type)
        {
            var objectName = type.Name;
            if (ObjectPropertyInfos.TryGetValue(objectName, out var propertyInfos)) return propertyInfos;

            propertyInfos = type.GetProperties().ToList();
            ObjectPropertyInfos[objectName] = propertyInfos;
            return propertyInfos;
        }

        /// <summary>
        /// 获取网络对象的头部信息
        /// </summary>
        /// <param name="netObjectType">网络对象类型</param>
        /// <returns>网络对象头部属性</returns>
        /// <exception cref="Exception">当类型未标记 NetHeadAttribute 时抛出异常</exception>
        public static NetHeadAttribute GetNetObjectHead(this Type netObjectType)
        {
            var attribute = netObjectType.GetCustomAttribute<NetHeadAttribute>();
            return attribute ?? throw new Exception(
                $"{netObjectType.Name} has not been marked with the attribute {nameof(NetHeadAttribute)}");
        }

        /// <summary>
        /// 从字节数组中读取网络对象头部信息
        /// </summary>
        /// <param name="buffer">字节数组</param>
        /// <param name="readIndex">读取起始索引</param>
        /// <param name="netObjectHeadInfo">输出的网络对象头部信息</param>
        /// <returns>是否成功读取头部信息</returns>
        public static bool ReadHead(this byte[] buffer, ref int readIndex, out NetHeadInfo netObjectHeadInfo)
        {
            if (ReadHead(buffer.AsSpan(readIndex), out netObjectHeadInfo, out var bytesConsumed))
            {
                readIndex += bytesConsumed;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 从Span<byte>中读取网络对象头部信息（高性能版本）
        /// </summary>
        /// <param name="span">字节Span</param>
        /// <param name="netObjectHeadInfo">输出的网络对象头部信息</param>
        /// <param name="bytesConsumed">消耗的字节数</param>
        /// <returns>是否成功读取头部信息</returns>
        public static bool ReadHead(this Span<byte> span, out NetHeadInfo netObjectHeadInfo, out int bytesConsumed)
        {
            netObjectHeadInfo = null;
            bytesConsumed = 0;
            // 检查缓冲区长度是否足够
            if (span.Length < PacketHeadLen) return false;

            netObjectHeadInfo = new NetHeadInfo();

            // 使用Span<T>的Slice方法来避免不必要的内存拷贝
            // 读取缓冲区长度
            netObjectHeadInfo.BufferLen = BitConverter.ToInt32(span.Slice(0, sizeof(int)));
            // 读取系统ID
            netObjectHeadInfo.SystemId = BitConverter.ToInt64(span.Slice(sizeof(int), sizeof(long)));
            // 读取对象ID
            netObjectHeadInfo.ObjectId = BitConverter.ToUInt16(span.Slice(sizeof(int) + sizeof(long), sizeof(ushort)));
            // 读取对象版本
            netObjectHeadInfo.ObjectVersion = span[sizeof(int) + sizeof(long) + sizeof(ushort)];
            // 读取时间戳
            netObjectHeadInfo.UnixTimeMilliseconds = BitConverter.ToInt64(span.Slice(sizeof(int) + sizeof(long) + sizeof(ushort) + sizeof(byte), sizeof(long)));

            bytesConsumed = PacketHeadLen;
            return true;
        }

        /// <summary>
        /// 检查网络对象头部信息是否匹配指定的类型
        /// </summary>
        /// <typeparam name="T">要检查的类型</typeparam>
        /// <param name="netObjectHeadInfo">网络对象头部信息</param>
        /// <returns>是否匹配</returns>
        public static bool IsNetObject<T>(this NetHeadInfo netObjectHeadInfo)
        {
            var netObjectAttribute = GetNetObjectHead(typeof(T));
            return netObjectAttribute.Id == netObjectHeadInfo.ObjectId &&
                   netObjectAttribute.Version == netObjectHeadInfo.ObjectVersion;
        }
    }
}