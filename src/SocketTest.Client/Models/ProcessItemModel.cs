using CodeWF.Tools.Extensions;
using ReactiveUI;
using SocketDto.Enums;
using SocketDto.Response;
using System;

namespace SocketTest.Client.Models;

/// <summary>
///     操作系统进程信息
/// </summary>
public class ProcessItemModel : ReactiveObject
{
    private string? _commandLine;

    private short _cpu;

    private short _disk;

    private short _gpu;

    private GpuEngine _gpuEngine;

    private DateTime _lastUpdateTime;

    private short _memory;

    private string? _name;

    private short _network;

    private PowerUsage _powerUsage;

    private PowerUsage _powerUsageTrend;

    private string? _publisher;

    private ProcessStatus _status;

    private ProcessType _type;

    private DateTime _updateTime;

    // 用于批量更新的标志
    private bool _isUpdating;

    /// <summary>
    /// 开始批量更新，暂停UI通知
    /// </summary>
    public void BeginUpdate()
    {
        _isUpdating = true;
    }

    /// <summary>
    /// 结束批量更新，恢复UI通知
    /// </summary>
    public void EndUpdate()
    {
        _isUpdating = false;
        // 触发一次属性更改通知，通知UI更新
        this.RaisePropertyChanged(nameof(Cpu));
        this.RaisePropertyChanged(nameof(Memory));
        this.RaisePropertyChanged(nameof(Disk));
        this.RaisePropertyChanged(nameof(Network));
    }

    public ProcessItemModel()
    {
    }

    public ProcessItemModel(ProcessItem process, int timestampStartYear)
    {
        Update(process, timestampStartYear);
    }

    /// <summary>
    ///     进程ID
    /// </summary>
    public int PID { get; set; }

    /// <summary>
    ///     进程名称
    /// </summary>
    public string? Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    /// <summary>
    ///     进程类型
    /// </summary>
    public ProcessType Type
    {
        get => _type;
        set => this.RaiseAndSetIfChanged(ref _type, value);
    }

    /// <summary>
    ///     进程状态
    /// </summary>
    public ProcessStatus Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private AlarmStatus _alarmStatus;

    /// <summary>
    /// 进程一般状态
    /// </summary>
    public AlarmStatus AlarmStatus
    {
        get => _alarmStatus;
        set => this.RaiseAndSetIfChanged(ref _alarmStatus, value);
    }

    /// <summary>
    ///     发布者
    /// </summary>
    public string? Publisher
    {
        get => _publisher;
        set => this.RaiseAndSetIfChanged(ref _publisher, value);
    }

    /// <summary>
    ///     命令行
    /// </summary>
    public string? CommandLine
    {
        get => _commandLine;
        set => this.RaiseAndSetIfChanged(ref _commandLine, value);
    }

    /// <summary>
    ///     CPU使用率
    /// </summary>
    public short Cpu
    {
        get => _cpu;
        set
        {
            _cpu = value;
            if (!_isUpdating)
                this.RaisePropertyChanged(nameof(Cpu));
        }
    }

    /// <summary>
    ///     内存使用大小
    /// </summary>
    public short Memory
    {
        get => _memory;
        set
        {
            _memory = value;
            if (!_isUpdating)
                this.RaisePropertyChanged(nameof(Memory));
        }
    }

    /// <summary>
    ///     磁盘使用大小
    /// </summary>
    public short Disk
    {
        get => _disk;
        set
        {
            _disk = value;
            if (!_isUpdating)
                this.RaisePropertyChanged(nameof(Disk));
        }
    }

    /// <summary>
    ///     网络使用值
    /// </summary>
    public short Network
    {
        get => _network;
        set
        {
            _network = value;
            if (!_isUpdating)
                this.RaisePropertyChanged(nameof(Network));
        }
    }

    /// <summary>
    ///     GPU
    /// </summary>
    public short Gpu
    {
        get => _gpu;
        set => this.RaiseAndSetIfChanged(ref _gpu, value);
    }

    /// <summary>
    ///     GPU引擎
    /// </summary>
    public GpuEngine GpuEngine
    {
        get => _gpuEngine;
        set => this.RaiseAndSetIfChanged(ref _gpuEngine, value);
    }

    /// <summary>
    ///     电源使用情况
    /// </summary>
    public PowerUsage PowerUsage
    {
        get => _powerUsage;
        set => this.RaiseAndSetIfChanged(ref _powerUsage, value);
    }

    /// <summary>
    ///     电源使用情况趋势
    /// </summary>
    public PowerUsage PowerUsageTrend
    {
        get => _powerUsageTrend;
        set => this.RaiseAndSetIfChanged(ref _powerUsageTrend, value);
    }

    /// <summary>
    ///     上次更新时间
    /// </summary>
    public DateTime LastUpdateTime
    {
        get => _lastUpdateTime;
        set => this.RaiseAndSetIfChanged(ref _lastUpdateTime, value);
    }

    /// <summary>
    ///     更新时间
    /// </summary>
    public DateTime UpdateTime
    {
        get => _updateTime;
        set => this.RaiseAndSetIfChanged(ref _updateTime, value);
    }

    public void Update(ProcessItem process, int timestampStartYear)
    {
        try
        {
            PID = process.Pid;

            Name = process.Name ?? "未知进程";
            Publisher = process.Publisher ?? "未知发布者";
            CommandLine = process.CommandLine ?? "";

            Cpu = process.Cpu;
            Memory = process.Memory;
            Disk = process.Disk;
            Network = process.Network;
            Gpu = process.Gpu;

            // 安全地进行枚举转换
            GpuEngine = Enum.IsDefined(typeof(GpuEngine), (int)process.GpuEngine) ? (GpuEngine)process.GpuEngine : GpuEngine.None;
            PowerUsage = Enum.IsDefined(typeof(PowerUsage), (int)process.PowerUsage) ? (PowerUsage)process.PowerUsage : PowerUsage.Low;
            PowerUsageTrend = Enum.IsDefined(typeof(PowerUsage), (int)process.PowerUsageTrend) ? (PowerUsage)process.PowerUsageTrend : PowerUsage.Low;
            Type = Enum.IsDefined(typeof(ProcessType), (int)process.Type) ? (ProcessType)process.Type : ProcessType.Application;
            Status = Enum.IsDefined(typeof(ProcessStatus), (int)process.ProcessStatus) ? (ProcessStatus)process.ProcessStatus : ProcessStatus.New;
            AlarmStatus = Enum.IsDefined(typeof(AlarmStatus), (int)process.AlarmStatus) ? (AlarmStatus)process.AlarmStatus : AlarmStatus.Normal;

            // 安全地进行时间转换
            try
            {
                LastUpdateTime = process.LastUpdateTime.FromSpecialUnixTimeSecondsToDateTime(timestampStartYear);
                UpdateTime = process.UpdateTime.FromSpecialUnixTimeSecondsToDateTime(timestampStartYear);
            }
            catch (Exception)
            {
                LastUpdateTime = DateTime.Now;
                UpdateTime = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            // 记录错误但不中断程序
            System.Diagnostics.Debug.WriteLine($"更新进程信息失败: {ex.Message}");
        }
    }

    public void Update(short cpu, short memory, short disk, short network)
    {
        // 使用批量更新减少UI通知次数
        BeginUpdate();
        try
        {
            Cpu = cpu;
            Memory = memory;
            Disk = disk;
            Network = network;
        }
        finally
        {
            EndUpdate();
        }
    }

    public void Update(int timestampStartYear, byte processStatus, byte alarmStatus, short gpu, byte gpuEngine, byte powerUsage, byte powerUsageTrend, uint updateTime)
    {
        try
        {
            // 安全地进行枚举转换
            Status = Enum.IsDefined(typeof(ProcessStatus), processStatus) ? (ProcessStatus)processStatus : ProcessStatus.New;
            AlarmStatus = Enum.IsDefined(typeof(AlarmStatus), alarmStatus) ? (AlarmStatus)alarmStatus : AlarmStatus.Normal;
            Gpu = gpu;
            GpuEngine = Enum.IsDefined(typeof(GpuEngine), gpuEngine) ? (GpuEngine)gpuEngine : GpuEngine.None;
            PowerUsage = Enum.IsDefined(typeof(PowerUsage), powerUsage) ? (PowerUsage)powerUsage : PowerUsage.Low;
            PowerUsageTrend = Enum.IsDefined(typeof(PowerUsage), powerUsageTrend) ? (PowerUsage)powerUsageTrend : PowerUsage.Low;

            LastUpdateTime = UpdateTime;

            // 安全地进行时间转换
            try
            {
                UpdateTime = updateTime.FromSpecialUnixTimeSecondsToDateTime(timestampStartYear);
            }
            catch (Exception)
            {
                UpdateTime = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            // 记录错误但不中断程序
            System.Diagnostics.Debug.WriteLine($"更新进程一般信息失败: {ex.Message}");
        }
    }
}