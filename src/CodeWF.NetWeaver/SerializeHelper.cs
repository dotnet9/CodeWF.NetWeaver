using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver
{
    public partial class SerializeHelper
    {
        private static readonly ConcurrentDictionary<string, List<PropertyInfo>> ObjectPropertyInfos =
            new ConcurrentDictionary<string, List<PropertyInfo>>();

        private static readonly List<string> ComplexTypeNames = new List<string>()
        {
            typeof(List<>).Name,
            typeof(Dictionary<,>).Name
        };

        public static Encoding DefaultEncoding = Encoding.UTF8;

        private static List<PropertyInfo> GetProperties(Type type)
        {
            var objectName = type.Name;
            if (ObjectPropertyInfos.TryGetValue(objectName, out var propertyInfos)) return propertyInfos;

            propertyInfos = type.GetProperties().ToList();
            ObjectPropertyInfos[objectName] = propertyInfos;
            return propertyInfos;
        }

        public static NetHeadAttribute GetNetObjectHead(this Type netObjectType)
        {
            var attribute = netObjectType.GetCustomAttribute<NetHeadAttribute>();
            return attribute ?? throw new Exception(
                $"{netObjectType.Name} has not been marked with the attribute {nameof(NetHeadAttribute)}");
        }

        public static bool ReadHead(this byte[] buffer, ref int readIndex, out NetHeadInfo netObjectHeadInfo)
        {
            netObjectHeadInfo = null;
            if (buffer.Length < readIndex + PacketHeadLen) return false;

            netObjectHeadInfo = new NetHeadInfo();

            netObjectHeadInfo.BufferLen = BitConverter.ToInt32(buffer, readIndex);
            readIndex += sizeof(int);

            netObjectHeadInfo.SystemId = BitConverter.ToInt64(buffer, readIndex);
            readIndex += sizeof(long);

            netObjectHeadInfo.ObjectId = BitConverter.ToUInt16(buffer, readIndex);
            readIndex += sizeof(ushort);

            netObjectHeadInfo.ObjectVersion = buffer[readIndex];
            readIndex += sizeof(byte);

            netObjectHeadInfo.UnixTimeMilliseconds = BitConverter.ToInt64(buffer, readIndex);
            readIndex += sizeof(long);

            return true;
        }

        public static bool IsNetObject<T>(this NetHeadInfo netObjectHeadInfo)
        {
            var netObjectAttribute = GetNetObjectHead(typeof(T));
            return netObjectAttribute.Id == netObjectHeadInfo.ObjectId &&
                   netObjectAttribute.Version == netObjectHeadInfo.ObjectVersion;
        }
    }
}