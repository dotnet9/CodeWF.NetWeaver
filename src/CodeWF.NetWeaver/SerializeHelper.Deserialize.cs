using CodeWF.NetWeaver.Base;
using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace CodeWF.NetWeaver;

/// <summary>
/// SerializeHelper 的反序列化部分实现
/// </summary>
public partial class SerializeHelper
{
    /// <summary>
    /// 从字节数组反序列化对象
    /// </summary>
    /// <typeparam name="T">要反序列化的对象类型</typeparam>
    /// <param name="buffer">字节数组</param>
    /// <returns>反序列化后的对象</returns>
    public static T Deserialize<T>(this byte[] buffer) where T : new()
    {
        return DeserializeObject<T>(buffer, PacketHeadLen);
    }

    /// <summary>
    /// 从字节数组反序列化对象
    /// </summary>
    /// <typeparam name="T">要反序列化的对象类型</typeparam>
    /// <param name="buffer">字节数组</param>
    /// <param name="readIndex">读取起始索引</param>
    /// <returns>反序列化后的对象</returns>
    public static T DeserializeObject<T>(this byte[] buffer, int readIndex = 0) where T : new()
    {
        using var stream = new MemoryStream(buffer, readIndex, buffer.Length - readIndex);
        using var reader = new BinaryReader(stream);
        var data = new T();
        DeserializeProperties(reader, data);
        return data;
    }

    /// <summary>
    /// 从字节数组反序列化对象
    /// </summary>
    /// <param name="buffer">字节数组</param>
    /// <param name="type">要反序列化的对象类型</param>
    /// <param name="readIndex">读取起始索引</param>
    /// <returns>反序列化后的对象</returns>
    public static object? DeserializeObject(this byte[] buffer, Type type, int readIndex = 0)
    {
        using var stream = new MemoryStream(buffer, readIndex, buffer.Length - readIndex);
        using var reader = new BinaryReader(stream);
        return DeserializeValue(reader, type);
    }

    /// <summary>
    /// 创建实例
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>创建的实例</returns>
    public static object CreateInstance(Type type)
    {
        // 这里复用前面统一的集合识别逻辑，避免在多个地方重复判断 List/Dictionary/接口集合。
        if (!TryGetCollectionMetadata(type, out var itemTypes, out var isDictionary))
        {
            throw new InvalidOperationException($"Type {type.FullName} is not a supported collection type.");
        }

        return CreateCollectionInstance(type, itemTypes, isDictionary);
    }

    /// <summary>
    /// 反序列化对象的所有属性
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="reader">BinaryReader 实例</param>
    /// <param name="data">要反序列化的对象</param>
    private static void DeserializeProperties<T>(BinaryReader reader, T data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        var properties = GetProperties(data.GetType());
        foreach (var property in properties)
        {
            if (property.GetCustomAttribute(typeof(NetIgnoreMemberAttribute)) is NetIgnoreMemberAttribute _)
                continue;

            try
            {
                var value = DeserializeValue(reader, property.PropertyType);
                property.SetValue(data, value);
            }
            catch (Exception ex)
            {
                throw new Exception($"Analyze \"{property.Name}\" property anomaly", ex);
            }
        }
    }

    /// <summary>
    /// 根据类型反序列化值
    /// </summary>
    /// <param name="reader">BinaryReader 实例</param>
    /// <param name="propertyType">属性类型</param>
    /// <returns>反序列化后的值</returns>
    private static object? DeserializeValue(BinaryReader reader, Type propertyType)
    {
        object? value;

        // IsArray 用来判断是否为数组类型，例如 int[]、string[]。
        if (IsScalarType(propertyType))
            value = DeserializeBaseValue(reader, propertyType);
        else if (propertyType.IsArray)
            value = DeserializeArrayValue(reader, propertyType);
        else if (TryGetCollectionMetadata(propertyType, out _, out _))
            value = DeserializeComplexValue(reader, propertyType);
        else
            value = DeserializeObject(reader, propertyType);

        return value;
    }

    /// <summary>
    /// 反序列化基本类型、字符串和枚举
    /// </summary>
    /// <param name="reader">BinaryReader 实例</param>
    /// <param name="propertyType">属性类型</param>
    /// <returns>反序列化后的值</returns>
    /// <exception cref="Exception">当遇到不支持的类型时抛出</exception>
    private static object DeserializeBaseValue(BinaryReader reader, Type propertyType)
    {
        if (propertyType.IsEnum)
        {
            // Enum.ToObject 会把底层整数值重新包装成指定的枚举类型，
            // 例如把 2 还原成 SampleEnum.SecondValue。
            return Enum.ToObject(propertyType, reader.ReadInt32());
        }

        if (propertyType == typeof(byte)) return reader.ReadByte();
        if (propertyType == typeof(char)) return reader.ReadChar();
        if (propertyType == typeof(sbyte)) return reader.ReadSByte();

        if (propertyType == typeof(short)) return reader.ReadInt16();

        if (propertyType == typeof(ushort)) return reader.ReadUInt16();

        if (propertyType == typeof(int)) return reader.ReadInt32();

        if (propertyType == typeof(uint)) return reader.ReadUInt32();

        if (propertyType == typeof(long)) return reader.ReadInt64();

        if (propertyType == typeof(ulong)) return reader.ReadUInt64();

        if (propertyType == typeof(float)) return reader.ReadSingle();

        if (propertyType == typeof(double)) return reader.ReadDouble();

        if (propertyType == typeof(decimal)) return reader.ReadDecimal();

        if (propertyType == typeof(string)) return reader.ReadString();

        if (propertyType == typeof(bool)) return reader.ReadBoolean();

        throw new Exception($"Unsupported data type: {propertyType.Name}");
    }

    /// <summary>
    /// 反序列化复杂类型（如 List、Dictionary）
    /// </summary>
    /// <param name="reader">BinaryReader 实例</param>
    /// <param name="propertyType">属性类型</param>
    /// <returns>反序列化后的对象</returns>
    private static object DeserializeComplexValue(BinaryReader reader, Type propertyType)
    {
        var count = reader.ReadInt32();
        // GetGenericArguments() 用来取出泛型参数。
        // 例如 List<int> 得到 [int]，Dictionary<string, double> 得到 [string, double]。
        var genericArguments = propertyType.GetGenericArguments();
        var complexObj = CreateInstance(propertyType);

        for (var i = 0; i < count; i++)
        {
            var key = DeserializeValue(reader, genericArguments[0]);
            switch (genericArguments.Length)
            {
                case 1:
                    // 反序列化列表时，这里的 key 实际上就是列表项本身。
                    (complexObj as IList)?.Add(key);
                    break;
                case 2:
                {
                    var value = DeserializeValue(reader, genericArguments[1]);
                    if (key == null)
                    {
                        throw new InvalidDataException($"Dictionary key for {propertyType.FullName} cannot be null.");
                    }

                    (complexObj as IDictionary)?[key] = value;
                    break;
                }
            }
        }

        return complexObj;
    }

    /// <summary>
    /// 反序列化数组
    /// </summary>
    /// <param name="reader">BinaryReader 实例</param>
    /// <param name="propertyType">数组类型</param>
    /// <returns>反序列化后的数组</returns>
    private static object? DeserializeArrayValue(BinaryReader reader, Type propertyType)
    {
        var length = reader.ReadInt32();
        // GetElementType() 返回数组元素类型，例如 int[] 返回 int。
        var elementType = propertyType.GetElementType();
        if (elementType == null) return null;
        var array = Array.CreateInstance(elementType, length);
        for (var i = 0; i < length; i++)
        {
            var value = DeserializeValue(reader, elementType);

            array.SetValue(value, i);
        }

        return array;
    }

    /// <summary>
    /// 反序列化对象
    /// </summary>
    /// <param name="reader">BinaryReader 实例</param>
    /// <param name="type">对象类型</param>
    /// <returns>反序列化后的对象</returns>
    private static object? DeserializeObject(BinaryReader reader, Type type)
    {
        // Activator.CreateInstance(type) 会按运行时类型动态创建实例，
        // 适合这里这种“编译期不知道具体 DTO 类型”的反射场景。
        var data = Activator.CreateInstance(type)
                   ?? throw new InvalidOperationException($"Cannot create instance of {type.FullName}.");
        DeserializeProperties(reader, data);
        return data;
    }
}
