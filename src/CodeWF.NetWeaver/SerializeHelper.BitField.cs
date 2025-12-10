using System;
using System.Reflection;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver;

/// <summary>
/// SerializeHelper 的位字段处理部分实现
/// </summary>
public partial class SerializeHelper
{
    /// <summary>
    /// 将对象的位字段序列化为字节数组
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="obj">要序列化的对象</param>
    /// <returns>包含位字段数据的字节数组</returns>
    public static byte[] FieldObjectBuffer<T>(this T obj) where T : class
    {
        var properties = typeof(T).GetProperties();
        var totalSize = 0;

        foreach (var property in properties)
        {
            if (!Attribute.IsDefined(property, typeof(NetFieldOffsetAttribute))) continue;

            var offsetAttribute =
                (NetFieldOffsetAttribute)property.GetCustomAttribute(typeof(NetFieldOffsetAttribute));
            totalSize = Math.Max(totalSize, offsetAttribute.Offset + offsetAttribute.Size);
        }

        var bufferLength = (int)Math.Ceiling((double)totalSize / 8);
        var buffer = new byte[bufferLength];

        foreach (var property in properties)
        {
            if (!Attribute.IsDefined(property, typeof(NetFieldOffsetAttribute))) continue;

            var offsetAttribute =
                (NetFieldOffsetAttribute)property.GetCustomAttribute(typeof(NetFieldOffsetAttribute));
            var value = property.GetValue(obj);
            SetBitValue(ref buffer, Convert.ToInt32(value), offsetAttribute.Offset, offsetAttribute.Size);
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
        var properties = typeof(T).GetProperties();

        foreach (var property in properties)
        {
            if (!Attribute.IsDefined(property, typeof(NetFieldOffsetAttribute))) continue;

            var offsetAttribute =
                (NetFieldOffsetAttribute)property.GetCustomAttribute(typeof(NetFieldOffsetAttribute));
            var value = GetValueFromBit(buffer, offsetAttribute.Offset, offsetAttribute.Size,
                property.PropertyType);
            property.SetValue(obj, value);
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

        return Convert.ChangeType(bitValue, propertyType); // 根据属性类型进行转换
    }
}