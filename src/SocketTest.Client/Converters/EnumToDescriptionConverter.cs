using Avalonia.Data;
using Avalonia.Data.Converters;
using CodeWF.Tools.Extensions;
using System;
using System.Globalization;

namespace SocketTest.Client.Converters;

/// <summary>
/// 枚举转描述信息转换器，将枚举值转换为其描述文本
/// </summary>
public class EnumToDescriptionConverter : IValueConverter
{
    /// <summary>
    /// 将枚举值转换为其描述文本
    /// </summary>
    /// <param name="value">枚举值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">参数</param>
    /// <param name="culture">文化信息</param>
    /// <returns>枚举的描述文本</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue) return value;

        return enumValue.GetDescription();
    }

    /// <summary>
    /// 反向转换（未实现）
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return BindingOperations.DoNothing;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!enumType.IsEnum)
        {
            return BindingOperations.DoNothing;
        }

        if (value.GetType() == enumType)
        {
            return value;
        }

        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return BindingOperations.DoNothing;
        }

        foreach (var name in Enum.GetNames(enumType))
        {
            var enumValue = (Enum)Enum.Parse(enumType, name, false);
            if (string.Equals(enumValue.GetDescription(), text, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                return enumValue;
            }
        }

        return Enum.TryParse(enumType, text, true, out var result)
            ? result
            : BindingOperations.DoNothing;
    }
}
