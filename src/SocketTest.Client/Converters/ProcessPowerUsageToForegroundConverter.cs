using Avalonia.Data.Converters;
using Avalonia.Media;
using SocketDto.Enums;
using System;
using System.Globalization;

namespace SocketTest.Client.Converters;

/// <summary>
/// 进程功耗使用转前景色转换器，根据功耗使用返回对应颜色
/// </summary>
public class ProcessPowerUsageToForegroundConverter : IValueConverter
{
    /// <summary>
    /// 根据功耗使用类型返回对应的颜色
    /// </summary>
    /// <param name="value">功耗使用值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">参数</param>
    /// <param name="culture">文化信息</param>
    /// <returns>前景色画刷</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return Brushes.Green;

        var powerUsageType =
            (PowerUsage)Enum.Parse(typeof(PowerUsage), value.ToString()!);
        return powerUsageType switch
        {
            PowerUsage.VeryLow or PowerUsage.Low => Brushes.LightGreen,
            PowerUsage.Moderate => Brushes.Green,
            PowerUsage.High => Brushes.DarkOrange,
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