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

        var netObjectInfo = data.GetType().GetNetObjectHead();
        var bodyBuffer = data.SerializeObject();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, DefaultEncoding);
        writer.Write(PacketHeadLen + bodyBuffer.Length);
        writer.Write(systemId);
        writer.Write(netObjectInfo.Id);
        writer.Write(netObjectInfo.Version);
        writer.Write(sendTime == default
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            : sendTime.ToUnixTimeMilliseconds());

        writer.Write(bodyBuffer);

        return stream.ToArray();
    }

    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="data">要序列化的对象</param>
    /// <returns>序列化后的字节数组</returns>
    public static byte[] SerializeObject<T>(this T data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, DefaultEncoding);
        SerializeValue(writer, data, typeof(T));
        return stream.ToArray();
    }

    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    /// <param name="data">要序列化的对象</param>
    /// <param name="type">对象类型</param>
    /// <returns>序列化后的字节数组</returns>
    public static byte[] SerializeObject(this object data, Type type)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, DefaultEncoding);
        SerializeValue(writer, data, type);
        return stream.ToArray();
    }

    /// <summary>
    /// 序列化对象的所有属性
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="data">要序列化的对象</param>
    private static void SerializeProperties<T>(BinaryWriter writer, T data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

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
        // PropertyInfo.GetValue(...) 是反射取值，用属性元数据从对象里读出当前属性值。
        var propertyValue = property.GetValue(data);
        SerializeValue(writer, propertyValue, propertyType);
    }

    /// <summary>
    /// 根据类型序列化值
    /// </summary>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="value">要序列化的值</param>
    /// <param name="valueType">值的类型</param>
    private static void SerializeValue(BinaryWriter writer, object? value, Type valueType)
    {
        if (IsScalarType(valueType))
            SerializeBaseValue(writer, value, valueType);
        // IsArray 只负责识别“是否是数组”，元素类型还要再通过 GetElementType() 读取。
        else if (valueType.IsArray)
            SerializeArrayValue(writer, value, valueType);
        else if (TryGetCollectionMetadata(valueType, out var genericArguments, out var isDictionary))
            SerializeComplexValue(writer, value, genericArguments, isDictionary);
        else if (value != null)
            SerializeProperties(writer, value);
        else
            throw new InvalidOperationException(
                $"Reference type {valueType.FullName} is null. Non-collection reference types do not support null serialization.");
    }

    /// <summary>
    /// 序列化基本类型、字符串和枚举（性能优化版本，直接转换避免 Parse 开销）
    /// </summary>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="value">要序列化的值</param>
    /// <param name="valueType">值的类型</param>
    /// <exception cref="Exception">当遇到不支持的类型时抛出</exception>
    private static void SerializeBaseValue(BinaryWriter writer, object? value, Type valueType)
    {
        if (valueType.IsEnum)
        {
            writer.Write(Convert.ToInt32(value));
        }
        else if (valueType == typeof(byte))
        {
            writer.Write(value == null ? (byte)0 : (byte)value);
        }
        else if (valueType == typeof(char))
        {
            writer.Write(value == null ? '\0' : (char)value);
        }
        else if (valueType == typeof(sbyte))
        {
            writer.Write(value == null ? (sbyte)0 : (sbyte)value);
        }
        else if (valueType == typeof(short))
        {
            writer.Write(value == null ? (short)0 : (short)value);
        }
        else if (valueType == typeof(ushort))
        {
            writer.Write(value == null ? (ushort)0 : (ushort)value);
        }
        else if (valueType == typeof(int))
        {
            writer.Write(value == null ? 0 : (int)value);
        }
        else if (valueType == typeof(uint))
        {
            writer.Write(value == null ? 0u : (uint)value);
        }
        else if (valueType == typeof(long))
        {
            writer.Write(value == null ? 0L : (long)value);
        }
        else if (valueType == typeof(ulong))
        {
            writer.Write(value == null ? 0UL : (ulong)value);
        }
        else if (valueType == typeof(float))
        {
            writer.Write(value == null ? 0f : (float)value);
        }
        else if (valueType == typeof(double))
        {
            writer.Write(value == null ? 0.0 : (double)value);
        }
        else if (valueType == typeof(decimal))
        {
            writer.Write(value == null ? 0m : (decimal)value);
        }
        else if (valueType == typeof(string))
        {
            writer.Write(value?.ToString() ?? string.Empty);
        }
        else if (valueType == typeof(bool))
        {
            writer.Write(value != null && (bool)value);
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
    private static void SerializeArrayValue(BinaryWriter writer, object? value, Type valueType)
    {
        if (value == null)
        {
            writer.Write(0);
            return;
        }

        var array = (Array)value;
        var length = array.Length;
        writer.Write(length);

        // GetElementType() 用来拿到数组的元素类型，后面每一项都按这个类型递归序列化。
        var elementType = valueType.GetElementType()!;
        for (var i = 0; i < length; i++)
        {
            var elementValue = array.GetValue(i);
            SerializeValue(writer, elementValue, elementType);
        }
    }

    /// <summary>
    /// 序列化复杂类型（如 List、Dictionary）
    /// </summary>
    /// <param name="writer">BinaryWriter 实例</param>
    /// <param name="value">要序列化的对象</param>
    /// <param name="valueType">对象类型</param>
    private static void SerializeComplexValue(BinaryWriter writer, object? value, Type[] genericArguments,
        bool isDictionary)
    {
        if (value == null)
        {
            writer.Write(0);
            return;
        }

        if (!isDictionary && value is IList list)
        {
            writer.Write(list.Count);
            foreach (var item in list)
            {
                SerializeValue(writer, item, genericArguments[0]);
            }

            return;
        }

        if (isDictionary && value is IDictionary dictionary)
        {
            writer.Write(dictionary.Count);
            foreach (DictionaryEntry item in dictionary)
            {
                SerializeValue(writer, item.Key, genericArguments[0]);
                SerializeValue(writer, item.Value, genericArguments[1]);
            }

            return;
        }

        throw new InvalidOperationException($"Unsupported collection value for {string.Join(", ", genericArguments.Select(x => x.Name))}.");
    }
}
