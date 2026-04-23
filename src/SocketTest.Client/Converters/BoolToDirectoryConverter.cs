using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SocketTest.Client.Converters;

public class BoolToDirectoryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirectory)
        {
            return isDirectory ? "目录" : "文件";
        }
        return "未知";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}