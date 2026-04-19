using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver;

/// <summary>
/// SerializeHelper 的位字段处理部分实现
/// </summary>
public partial class SerializeHelper
{
    /// <summary>
    /// 位字段属性信息结构，包含属性及其偏移量属性
    /// </summary>
    private class BitFieldPropertyInfo
    {
        /// <summary>
        /// 属性信息
        /// </summary>
        public required PropertyInfo Property { get; init; }

        /// <summary>
        /// 位偏移量
        /// </summary>
        public required NetFieldOffsetAttribute OffsetAttribute { get; init; }
    }

    /// <summary>
    /// 缓存位字段属性信息的字典，键为类型名称，提高反射效率
    /// </summary>
    private static readonly ConcurrentDictionary<string, BitFieldPropertyInfo[]> BitFieldPropertiesCache = new();

    /// <summary>
    /// 获取指定类型的位字段属性信息列表，使用缓存提高效率
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <returns>位字段属性信息列表</returns>
    private static BitFieldPropertyInfo[] GetBitFieldProperties<T>()
    {
        var typeName = typeof(T).Name;
        if (BitFieldPropertiesCache.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        var properties = typeof(T).GetProperties();
        var bitFieldProperties = new List<BitFieldPropertyInfo>();

        foreach (var property in properties)
        {
            if (!Attribute.IsDefined(property, typeof(NetFieldOffsetAttribute)))
            {
                continue;
            }

            var offsetAttribute = (NetFieldOffsetAttribute)property.GetCustomAttribute(typeof(NetFieldOffsetAttribute))!;
            bitFieldProperties.Add(new BitFieldPropertyInfo
            {
                Property = property,
                OffsetAttribute = offsetAttribute
            });
        }

        BitFieldPropertiesCache[typeName] = bitFieldProperties.ToArray();
        return bitFieldProperties.ToArray();
    }

    /// <summary>
    /// 将对象的位字段序列化为字节数组
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
    /// 从字节数组反序列化为位字段对象
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
    /// 设置位字段值到字节数组中
    /// </summary>
    /// <param name="buffer">字节数组</param>
    /// <param name="value">要设置的值</param>
    /// <param name="offset">位偏移量</param>
    /// <param name="size">位大小</param>
    private static void SetBitValue(ref byte[] buffer, int value, int offset, int size)
    {
        var mask = (1 << size) - 1;
        buffer[offset / 8] |= (byte)((value & mask) << (offset % 8));
        if (offset % 8 + size > 8) buffer[offset / 8 + 1] |= (byte)((value & mask) >> (8 - offset % 8));
    }

    /// <summary>
    /// 从字节数组中获取位字段值
    /// </summary>
    /// <param name="buffer">字节数组</param>
    /// <param name="offset">位偏移量</param>
    /// <param name="size">位大小</param>
    /// <param name="propertyType">属性类型</param>
    /// <returns>位字段值</returns>
    private static object GetValueFromBit(byte[] buffer, int offset, int size, Type propertyType)
    {
        var mask = (1 << size) - 1;
        var bitValue = (buffer[offset / 8] >> (offset % 8)) & mask;
        if (offset % 8 + size > 8) bitValue |= (buffer[offset / 8 + 1] << (8 - offset % 8)) & mask;

        return Convert.ChangeType(bitValue, propertyType);
    }
}