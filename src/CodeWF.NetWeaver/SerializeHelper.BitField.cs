using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver;

/// <summary>
///     SerializeHelper 的位字段处理部分实现
/// </summary>
public partial class SerializeHelper
{
    /// <summary>
    ///     缓存位字段属性信息的字典，键为类型，提高反射效率
    /// </summary>
    private static readonly ConcurrentDictionary<Type, BitFieldPropertyInfo[]> BitFieldPropertiesCache = new();

    /// <summary>
    ///     获取指定类型的位字段属性信息列表，使用缓存提高效率
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <returns>位字段属性信息列表</returns>
    private static BitFieldPropertyInfo[] GetBitFieldProperties<T>()
    {
        var type = typeof(T);
        if (BitFieldPropertiesCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var properties = type.GetProperties()
            .OrderBy(property => property.MetadataToken);
        var bitFieldProperties = new List<BitFieldPropertyInfo>();

        foreach (var property in properties)
        {
            if (!Attribute.IsDefined(property, typeof(NetFieldOffsetAttribute)))
            {
                continue;
            }

            var offsetAttribute =
                (NetFieldOffsetAttribute)property.GetCustomAttribute(typeof(NetFieldOffsetAttribute))!;
            bitFieldProperties.Add(new BitFieldPropertyInfo
            {
                Property = property,
                OffsetAttribute = offsetAttribute
            });
        }

        var result = bitFieldProperties.ToArray();
        BitFieldPropertiesCache[type] = result;
        return result;
    }

    /// <summary>
    ///     将对象的位字段序列化为字节数组
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="obj">要序列化的对象</param>
    /// <returns>包含位字段数据的字节数组</returns>
    public static byte[] FieldObjectBuffer<T>(this T obj) where T : class
    {
        var bitFieldProperties = GetBitFieldProperties<T>();
        var totalSize = 0;

        foreach (var bp in bitFieldProperties)
        {
            totalSize = Math.Max(totalSize, bp.OffsetAttribute.Offset + bp.OffsetAttribute.Size);
        }

        if (totalSize == 0)
        {
            return [];
        }

        var bufferLength = (int)Math.Ceiling((double)totalSize / 8);
        var buffer = new byte[bufferLength];

        foreach (var bp in bitFieldProperties)
        {
            var value = bp.Property.GetValue(obj);
            SetBitValue(ref buffer, Convert.ToInt32(value), bp.OffsetAttribute.Offset, bp.OffsetAttribute.Size);
        }

        return buffer;
    }

    /// <summary>
    ///     从字节数组反序列化为位字段对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="buffer">包含位字段数据的字节数组</param>
    /// <returns>反序列化后的对象</returns>
    public static T ToFieldObject<T>(this byte[] buffer) where T : class, new()
    {
        var obj = new T();
        var bitFieldProperties = GetBitFieldProperties<T>();

        foreach (var bp in bitFieldProperties)
        {
            var value = GetValueFromBit(buffer, bp.OffsetAttribute.Offset, bp.OffsetAttribute.Size,
                bp.Property.PropertyType);
            bp.Property.SetValue(obj, value);
        }

        return obj;
    }

    /// <summary>
    ///     设置位字段值到字节数组中
    /// </summary>
    /// <param name="buffer">字节数组</param>
    /// <param name="value">要设置的值</param>
    /// <param name="offset">位偏移量</param>
    /// <param name="size">位大小</param>
    private static void SetBitValue(ref byte[] buffer, int value, int offset, int size)
    {
        ValidateBitField(offset, size);

        var rawValue = unchecked((uint)value);
        var mask = size == sizeof(int) * 8 ? uint.MaxValue : (1u << size) - 1u;
        rawValue &= mask;

        for (var bitIndex = 0; bitIndex < size; bitIndex++)
        {
            if (((rawValue >> bitIndex) & 1u) == 0)
            {
                continue;
            }

            var targetBit = offset + bitIndex;
            buffer[targetBit / 8] |= (byte)(1 << (targetBit % 8));
        }
    }

    /// <summary>
    ///     从字节数组中获取位字段值
    /// </summary>
    /// <param name="buffer">字节数组</param>
    /// <param name="offset">位偏移量</param>
    /// <param name="size">位大小</param>
    /// <param name="propertyType">属性类型</param>
    /// <returns>位字段值</returns>
    private static object GetValueFromBit(byte[] buffer, int offset, int size, Type propertyType)
    {
        ValidateBitField(offset, size);

        uint bitValue = 0;
        for (var bitIndex = 0; bitIndex < size; bitIndex++)
        {
            var sourceBit = offset + bitIndex;
            if ((buffer[sourceBit / 8] & (1 << (sourceBit % 8))) != 0)
            {
                bitValue |= 1u << bitIndex;
            }
        }

        if (propertyType.IsEnum)
        {
            return Enum.ToObject(propertyType, bitValue);
        }

        return Convert.ChangeType(bitValue, propertyType);
    }

    private static void ValidateBitField(int offset, int size)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Bit offset cannot be negative.");
        }

        if (size <= 0 || size > sizeof(int) * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Bit field size must be between 1 and 32.");
        }
    }

    /// <summary>
    ///     位字段属性信息结构，包含属性及其偏移量属性
    /// </summary>
    private class BitFieldPropertyInfo
    {
        /// <summary>
        ///     属性信息
        /// </summary>
        public required PropertyInfo Property { get; init; }

        /// <summary>
        ///     位偏移量
        /// </summary>
        public required NetFieldOffsetAttribute OffsetAttribute { get; init; }
    }
}
