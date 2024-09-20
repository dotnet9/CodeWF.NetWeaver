using CodeWF.NetWeaver.Base;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CodeWF.NetWeaver
{
    public partial class SerializeHelper
    {
        public static byte[] Serialize<T>(this T data, long systemId) where T : INetObject
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var netObjectInfo = GetNetObjectHead(data.GetType());
            var bodyBuffer = SerializeObject(data);
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, DefaultEncoding))
                {
                    writer.Write(PacketHeadLen + bodyBuffer.Length);
                    writer.Write(systemId);
                    writer.Write(netObjectInfo.Id);
                    writer.Write(netObjectInfo.Version);
                    writer.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    writer.Write(bodyBuffer);

                    return stream.ToArray();
                }
            }
        }

        public static byte[] SerializeObject<T>(this T data)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, DefaultEncoding))
                {
                    SerializeValue(writer, data, typeof(T));
                    return stream.ToArray();
                }
            }
        }

        public static byte[] SerializeObject(this object data, Type type)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, DefaultEncoding))
                {
                    SerializeValue(writer, data, type);
                    return stream.ToArray();
                }
            }
        }

        private static void SerializeProperties<T>(BinaryWriter writer, T data)
        {
            var properties = GetProperties(data.GetType()).Where(p=> p.GetCustomAttribute(typeof(NetIgnoreMemberAttribute)) == null);
            foreach (var property in properties)
            {
                SerializeProperty(writer, data, property);
            }
        }

        private static void SerializeProperty<T>(BinaryWriter writer, T data, PropertyInfo property)
        {
            var propertyType = property.PropertyType;
            var propertyValue = property.GetValue(data);
            SerializeValue(writer, propertyValue, propertyType);
        }

        private static void SerializeValue(BinaryWriter writer, object value, Type valueType)
        {
            if (valueType.IsPrimitive || valueType == typeof(string))
                SerializeBaseValue(writer, value, valueType);
            else if(valueType.IsArray)
                SerializeArrayValue(writer, value, valueType);
            else if (ComplexTypeNames.Contains(valueType.Name))
                SerializeComplexValue(writer, value, valueType);
            else
                SerializeProperties(writer, value);
        }

        private static void SerializeBaseValue(BinaryWriter writer, object value, Type valueType)
        {
            if (valueType == typeof(byte))
            {
                writer.Write(value == null ? default : byte.Parse(value.ToString()));
            }
            else if (valueType == typeof(short))
            {
                writer.Write(value == null ? default : short.Parse(value.ToString()));
            }
            else if (valueType == typeof(ushort))
            {
                writer.Write(value == null ? default : ushort.Parse(value.ToString()));
            }
            else if (valueType == typeof(int))
            {
                writer.Write(value == null ? default : int.Parse(value.ToString()));
            }
            else if (valueType == typeof(uint))
            {
                writer.Write(value == null ? default : uint.Parse(value.ToString()));
            }
            else if (valueType == typeof(long))
            {
                writer.Write(value == null ? default : long.Parse(value.ToString()));
            }
            else if (valueType == typeof(float))
            {
                writer.Write(value == null ? default : float.Parse(value.ToString()));
            }
            else if (valueType == typeof(double))
            {
                writer.Write(value == null ? default : double.Parse(value.ToString()));
            }
            else if (valueType == typeof(decimal))
            {
                writer.Write(value == null ? default : decimal.Parse(value.ToString()));
            }
            else if (valueType == typeof(string))
            {
                writer.Write(value == null ? string.Empty : value.ToString());
            }
            else
            {
                throw new Exception($"Unsupported data type: {valueType.Name}");
            }
        }
        private static void SerializeArrayValue(BinaryWriter writer, object value, Type valueType)
        {
            var length = 0;
            if (value == null)
            {
                writer.Write(length);
                return;
            }

            length = ((Array)value).Length;
            writer.Write(length);

            var elementType = valueType.GetElementType();
            for (var i = 0; i < length; i++)
            {
                var elementValue = ((Array)value).GetValue(i);
                SerializeValue(writer, elementValue, elementType);
            }
        }

        private static void SerializeComplexValue(BinaryWriter writer, object value, Type valueType)
        {
            var count = 0;
            if (value == null)
            {
                writer.Write(count);
                return;
            }


            var genericArguments = valueType.GetGenericArguments();
            if (value is IList list)
            {
                writer.Write(list.Count);
                foreach (var item in list)
                {
                    SerializeValue(writer, item, genericArguments[0]);
                }
            }
            else if (value is IDictionary dictionary)
            {
                writer.Write(dictionary.Count);
                foreach (DictionaryEntry item in dictionary)
                {
                    SerializeValue(writer, item.Key, genericArguments[0]);
                    SerializeValue(writer, item.Value, genericArguments[1]);
                }
            }
        }
    }
}