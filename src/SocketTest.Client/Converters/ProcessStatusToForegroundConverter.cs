using Avalonia.Data.Converters;
using Avalonia.Media;
using SocketDto.Enums;
using System;
using System.Globalization;

namespace SocketTest.Client.Converters;

/// <summary>
/// 进程状态转前景色转换器，根据进程状态返回对应颜色。
/// </summary>
public class ProcessStatusToForegroundConverter : IValueConverter
{
    /// <summary>
    /// 根据进程状态返回对应的前景色。
    /// </summary>
    /// <param name="value">进程状态值。</param>
    /// <param name="targetType">目标类型。</param>
    /// <param name="parameter">附加参数。</param>
    /// <param name="culture">区域性信息。</param>
    /// <returns>对应的前景画刷。</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ProcessStatus status)
        {
            return status switch
            {
                < ProcessStatus.Running => Brushes.CadetBlue,
                > ProcessStatus.Running => Brushes.Green,
                _ => Brushes.Red
            };
        }

        return Brushes.CadetBlue;
    }

    /// <summary>
    /// 反向转换，当前未实现。
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
