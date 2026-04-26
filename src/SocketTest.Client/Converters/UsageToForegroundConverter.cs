using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SocketTest.Client.Converters;

/// <summary>
/// 使用率转前景色转换器，根据使用率返回不同的颜色。
/// </summary>
public class UsageToForegroundConverter : IValueConverter
{
    /// <summary>
    /// 根据使用率返回对应的前景色。
    /// </summary>
    /// <param name="value">使用率值。</param>
    /// <param name="targetType">目标类型。</param>
    /// <param name="parameter">附加参数。</param>
    /// <param name="culture">区域性信息。</param>
    /// <returns>对应的前景画刷。</returns>
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
    /// 反向转换，当前未实现。
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
