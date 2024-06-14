using System;
using System.Reflection;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver
{
    public partial class SerializeHelper
    {
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
                dynamic value = property.GetValue(obj);
                SetBitValue(ref buffer, value, offsetAttribute.Offset, offsetAttribute.Size);
            }

            return buffer;
        }

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

        private static void SetBitValue(ref byte[] buffer, int value, int offset, int size)
        {
            var mask = (1 << size) - 1;
            buffer[offset / 8] |= (byte)((value & mask) << (offset % 8));
            if (offset % 8 + size > 8) buffer[offset / 8 + 1] |= (byte)((value & mask) >> (8 - offset % 8));
        }

        private static dynamic GetValueFromBit(byte[] buffer, int offset, int size, Type propertyType)
        {
            var mask = (1 << size) - 1;
            var bitValue = (buffer[offset / 8] >> (offset % 8)) & mask;
            if (offset % 8 + size > 8) bitValue |= (buffer[offset / 8 + 1] << (8 - offset % 8)) & mask;

            dynamic result = Convert.ChangeType(bitValue, propertyType); // 根据属性类型进行转换
            return result;
        }
    }
}