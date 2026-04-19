using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SocketTest.Client.Converters;

/// <summary>
/// 使用率转前景色转换器，根据使用率值返回不同的颜色
/// </summary>
public class UsageToForegroundConverter : IValueConverter
{
    /// <summary>
    /// 根据使用率值返回对应的颜色
    /// </summary>
    /// <param name="value">使用率值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">参数</param>
    /// <param name="culture">文化信息</param>
    /// <returns>前景色画刷</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || !short.TryParse(value.ToString(), out var bValue)) return Brushes.Green;

        var dValue = bValue * 1.0 / 10;
        return dValue switch
        {
            < 5 => Brushes.LightGreen,
            < 10 => Brushes.Green,
            < 20 => Brushes.DarkOrange,
            _ => Brushes.Red
        };
    }

    /// <summary>
    /// 反向转换（未实现）
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}