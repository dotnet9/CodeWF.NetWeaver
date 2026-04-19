using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SocketTest.Client.Converters;

/// <summary>
/// 使用率转格式化字符串转换器，将使用率值转换为百分比格式字符串
/// </summary>
public class UsageToFormatConverter : IValueConverter
{
    /// <summary>
    /// 将使用率值转换为百分比格式字符串
    /// </summary>
    /// <param name="value">使用率值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">参数</param>
    /// <param name="culture">文化信息</param>
    /// <returns>百分比格式字符串</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || !short.TryParse(value.ToString(), out var bValue)) return Brushes.Green;

        var dValue = bValue * 1.0 / 1000;
        return dValue.ToString("P1");
    }

    /// <summary>
    /// 反向转换（未实现）
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}