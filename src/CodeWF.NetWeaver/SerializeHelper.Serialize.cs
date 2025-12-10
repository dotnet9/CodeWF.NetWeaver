using CodeWF.NetWeaver.Base;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CodeWF.NetWeaver;

/// <summary>
/// SerializeHelper 的序列化部分实现
/// </summary>
public partial class SerializeHelper
{
    /// <summary>
    /// 序列化网络对象为字节数组
    /// </summary>
    /// <typeparam name="T">网络对象类型</typeparam>
    /// <param name="data">要序列化的对象</param>
    /// <param name="systemId">系统ID</param>
    /// <param name="sendTime">发送时间</param>
    /// <returns>序列化后的字节数组</returns>
    /// <exception cref="ArgumentNullException">当 data 为 null 时抛出</exception>
    public static byte[] Serialize<T>(this T data, long systemId, DateTimeOffset sendTime = default)
        where T : INetObject
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
                if (sendTime == default)
                {
                    writer.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
                else
                {
                    writer.Write(sendTime.ToUnixTimeMilliseconds());
                }

                writer.Write(bodyBuffer);

                return stream.ToArray();
            }
        }
    }

    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="data">要序列化的对象</param>
    /// <returns>序列化后的字节数组</returns>
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

    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    /// <param name="data">要序列化的对象</param>
    /// <param name="type">对象类型</param>
    /// <returns>序列化后的字节数组</returns>
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

    /// <summary>
    /// 序列化对象的所有属性
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="data">要序列化的对象</param>
    private static void SerializeProperties<T>(BinaryWriter writer, T data)
    {
        var properties = GetProperties(data.GetType())
            .Where(p => p.GetCustomAttribute(typeof(NetIgnoreMemberAttribute)) == null);
        foreach (var property in properties)
        {
            SerializeProperty(writer, data, property);
        }
    }

    /// <summary>
    /// 序列化单个属性
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="data">要序列化的对象</param>
    /// <param name="property">属性信息</param>
    private static void SerializeProperty<T>(BinaryWriter writer, T data, PropertyInfo property)
    {
        var propertyType = property.PropertyType;
        var propertyValue = property.GetValue(data);
        SerializeValue(writer, propertyValue, propertyType);
    }

    /// <summary>
    /// 根据类型序列化值
    /// </summary>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="value">要序列化的值</param>
    /// <param name="valueType">值的类型</param>
    private static void SerializeValue(BinaryWriter writer, object value, Type valueType)
    {
        if (valueType.IsPrimitive || valueType == typeof(string) || valueType.IsEnum)
            SerializeBaseValue(writer, value, valueType);
        else if (valueType.IsArray)
            SerializeArrayValue(writer, value, valueType);
        else if (ComplexTypeNames.Contains(valueType.Name))
            SerializeComplexValue(writer, value, valueType);
        else
            SerializeProperties(writer, value);
    }

    /// <summary>
    /// 序列化基本类型、字符串和枚举
    /// </summary>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="value">要序列化的值</param>
    /// <param name="valueType">值的类型</param>
    /// <exception cref="Exception">当遇到不支持的类型时抛出</exception>
    private static void SerializeBaseValue(BinaryWriter writer, object value, Type valueType)
    {
        if (valueType.IsEnum)
        {
            // 对于枚举类型，将其转换为整数进行序列化
            writer.Write(value == null ? 0 : Convert.ToInt32(value));
        }
        else if (valueType == typeof(byte))
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
        else if (valueType == typeof(bool))
        {
            writer.Write(value == null ? default : bool.Parse(value.ToString()));
        }
        else
        {
            throw new Exception($"Unsupported data type: {valueType.Name}");
        }
    }

    /// <summary>
    /// 序列化数组
    /// </summary>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="value">要序列化的数组</param>
    /// <param name="valueType">数组类型</param>
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

    /// <summary>
    /// 序列化复杂类型（如 List、Dictionary）
    /// </summary>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="value">要序列化的对象</param>
    /// <param name="valueType">对象类型</param>
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