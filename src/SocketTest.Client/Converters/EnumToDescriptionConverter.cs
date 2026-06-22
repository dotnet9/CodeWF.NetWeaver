using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using SocketDto.Enums;

namespace SocketTest.Client.Converters;

/// <summary>
///     枚举转描述信息转换器，将枚举值转换为其描述文本
/// </summary>
public class EnumToDescriptionConverter : IValueConverter
{
    /// <summary>
    ///     将枚举值转换为其描述文本
    /// </summary>
    /// <param name="value">枚举值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">参数</param>
    /// <param name="culture">文化信息</param>
    /// <returns>枚举的描述文本</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue)
        {
            return value;
        }

        return GetDescription(enumValue);
    }

    /// <summary>
    ///     反向转换（未实现）
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
            if (string.Equals(GetDescription(enumValue), text, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                return enumValue;
            }
        }

        return Enum.TryParse(enumType, text, true, out var result)
            ? result
            : BindingOperations.DoNothing;
    }

    private static string GetDescription(Enum value)
    {
        return value switch
        {
            AlarmStatus alarmStatus => GetAlarmStatusDescription(alarmStatus),
            GpuEngine.None => "无",
            GpuEngine.Gpu03D => "GPU 0 - 3D",
            PowerUsage.VeryLow => "非常低",
            PowerUsage.Low => "低",
            PowerUsage.Moderate => "中",
            PowerUsage.High => "高",
            PowerUsage.VeryHigh => "非常高",
            ProcessStatus.New => "新建状态",
            ProcessStatus.Ready => "就绪状态",
            ProcessStatus.Running => "运行状态",
            ProcessStatus.Blocked => "阻塞状态",
            ProcessStatus.Terminated => "终止状态",
            ProcessType.Application => "应用",
            ProcessType.BackgroundProcess => "后台进程",
            TerminalType.Server => "服务端",
            TerminalType.Client => "客户端",
            _ => value.ToString()
        };
    }

    private static string GetAlarmStatusDescription(AlarmStatus value)
    {
        if (value == AlarmStatus.Normal)
        {
            return "正常";
        }

        var names = new List<string>();
        if (value.HasFlag(AlarmStatus.Overtime))
        {
            names.Add("超时");
        }

        if (value.HasFlag(AlarmStatus.OverLimit))
        {
            names.Add("超限");
        }

        if (value.HasFlag(AlarmStatus.UserChanged))
        {
            names.Add("切换用户");
        }

        return names.Count == 0 ? value.ToString() : string.Join(",", names);
    }
}
