using Avalonia.Threading;
using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using CodeWF.Tools.Extensions;
using ReactiveUI;
using SocketDto;
using SocketDto.AutoCommand;
using SocketDto.Enums;
using SocketDto.Response;
using SocketTest.Client.Dtos;
using SocketTest.Client.Extensions;
using SocketTest.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace SocketTest.Client.ViewModels;

public class ProcessMonitorViewModel : ReactiveObject
{
    private readonly List<ProcessItemModel> _receivedProcesses = [];
    private int[]? _processIdArray;
    private Dictionary<int, ProcessItemModel>? _processIdAndItems;
    private string _tcpIp = "127.0.0.1";
    private int _tcpPort = 5000;
    private string _udpIp = "239.255.255.250";
    private int _udpPort = 11012;
    private long _systemId = 1000;

    public ProcessMonitorViewModel()
    {
        DisplayProcesses = new RangObservableCollection<ProcessItemModel>();
        HandleConnectTcpCommand = ReactiveCommand.CreateFromTask(HandleConnectTcpAsync);
        RefreshCommand = ReactiveCommand.Create(HandleRefreshCommand);
        RefreshAllCommand = ReactiveCommand.Create(HandleRefreshAllCommand);
        SendCorrectCommand = ReactiveCommand.Create(HandleSendCorrectCommand);
        SendDiffVersionCommand = ReactiveCommand.Create(HandleSendDiffVersionCommand);
        SendDiffPropsCommand = ReactiveCommand.Create(HandleSendDiffPropsCommand);

        EventBus.Default.Subscribe(this);
        Logger.Info("进程监控模块已初始化。");
    }

    public RangObservableCollection<ProcessItemModel> DisplayProcesses { get; }

    public string TcpIp
    {
        get => _tcpIp;
        set => this.RaiseAndSetIfChanged(ref _tcpIp, value);
    }

    public int TcpPort
    {
        get => _tcpPort;
        set => this.RaiseAndSetIfChanged(ref _tcpPort, value);
    }

    public string UdpIp
    {
        get => _udpIp;
        set
        {
            this.RaiseAndSetIfChanged(ref _udpIp, value);
            this.RaisePropertyChanged(nameof(UdpSummary));
        }
    }

    public int UdpPort
    {
        get => _udpPort;
        set
        {
            this.RaiseAndSetIfChanged(ref _udpPort, value);
            this.RaisePropertyChanged(nameof(UdpSummary));
        }
    }

    public long SystemId
    {
        get => _systemId;
        set => this.RaiseAndSetIfChanged(ref _systemId, value);
    }

    public bool IsRunning => TcpHelper.IsRunning;

    public string ConnectionSummary => TcpHelper.IsRunning
        ? $"已连接到 {TcpIp}:{TcpPort}"
        : "未连接到 TCP 服务端";

    public string ConnectButtonText => TcpHelper.IsRunning ? "断开连接" : "连接服务端";

    public string ProcessSummary => $"当前展示 {DisplayProcesses.Count} 个进程";

    public string UdpSummary => $"{UdpIp}:{UdpPort}";

    public TcpSocketClient TcpHelper { get; } = new();

    public UdpSocketClient UdpHelper { get; } = new();

    public ReactiveCommand<Unit, Unit> HandleConnectTcpCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshAllCommand { get; }

    public ReactiveCommand<Unit, Unit> SendCorrectCommand { get; }

    public ReactiveCommand<Unit, Unit> SendDiffVersionCommand { get; }

    public ReactiveCommand<Unit, Unit> SendDiffPropsCommand { get; }

    private async Task HandleConnectTcpAsync()
    {
        if (TcpHelper.IsRunning)
        {
            TcpHelper.Stop();
            this.RaisePropertyChanged(nameof(IsRunning));
            this.RaisePropertyChanged(nameof(ConnectionSummary));
            this.RaisePropertyChanged(nameof(ConnectButtonText));
            return;
        }

        var result = await TcpHelper.ConnectAsync("SocketTest.Client", TcpIp, TcpPort);
        if (!result.IsSuccess)
        {
            Logger.Warn(result.ErrorMessage ?? "TCP 连接失败。");
        }

        this.RaisePropertyChanged(nameof(IsRunning));
        this.RaisePropertyChanged(nameof(ConnectionSummary));
        this.RaisePropertyChanged(nameof(ConnectButtonText));
    }

    private void HandleRefreshCommand()
    {
        _ = TcpHelper.SendCommandAsync(new RequestProcessList());
    }

    private void HandleRefreshAllCommand()
    {
        _ = TcpHelper.SendCommandAsync(new RequestProcessList());
    }

    private void HandleSendCorrectCommand()
    {
        _ = TcpHelper.SendCommandAsync(new RequestStudentListCorrect());
    }

    private void HandleSendDiffVersionCommand()
    {
        _ = TcpHelper.SendCommandAsync(new RequestStudentListDiffVersion());
    }

    private void HandleSendDiffPropsCommand()
    {
        _ = TcpHelper.SendCommandAsync(new RequestStudentListDiffProps());
    }

    public void ReceivedSocketCommand(SocketCommand message)
    {
        if (message.IsCommand<ResponseTargetType>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseTargetType>());
        }
        else if (message.IsCommand<ResponseUdpAddress>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseUdpAddress>());
        }
        else if (message.IsCommand<ResponseServiceInfo>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseServiceInfo>());
        }
        else if (message.IsCommand<ResponseProcessIDList>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseProcessIDList>());
        }
        else if (message.IsCommand<ResponseProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<ResponseProcessList>());
        }
        else if (message.IsCommand<UpdateProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<UpdateProcessList>());
        }
        else if (message.IsCommand<ChangeProcessList>())
        {
            ReceivedSocketMessage(message.GetCommand<ChangeProcessList>());
        }
    }

    private void ReceivedSocketMessage(ResponseTargetType response)
    {
        var type = (TerminalType)Enum.Parse(typeof(TerminalType), response.Type.ToString());
        if (response.Type == (byte)TerminalType.Server)
        {
            Logger.Info($"连接目标已确认：{type.GetDescription()}。");
        }
    }

    private void ReceivedSocketMessage(ResponseUdpAddress response)
    {
        UdpIp = response.Ip ?? UdpIp;
        UdpPort = response.Port;
        Logger.Info($"已收到 UDP 组播地址：{UdpIp}:{UdpPort}");
        _ = UdpHelper.ConnectAsync("Server", UdpIp, UdpPort, string.Empty, SystemId);
    }

    private void ReceivedSocketMessage(ResponseServiceInfo response)
    {
        Logger.Info($"服务端信息：{response.OS}，时间基准年份：{response.TimestampStartYear}");
    }

    private void ReceivedSocketMessage(ResponseProcessIDList response)
    {
        _processIdArray = response.IDList;
        _processIdAndItems = _receivedProcesses.ToDictionary(process => process.PID, process => process);
    }

    private void ReceivedSocketMessage(ResponseProcessList response)
    {
        _receivedProcesses.Clear();
        foreach (var process in response.Processes ?? [])
        {
            _receivedProcesses.Add(new ProcessItemModel(process, 2020));
        }

        if (_processIdArray != null)
        {
            _processIdAndItems = _receivedProcesses
                .Where(process => _processIdArray.Contains(process.PID))
                .ToDictionary(process => process.PID, process => process);
        }

        Dispatcher.UIThread.Post(() =>
        {
            DisplayProcesses.Clear();
            DisplayProcesses.AddRange(_receivedProcesses);
            this.RaisePropertyChanged(nameof(ProcessSummary));
        });
    }

    private void ReceivedSocketMessage(UpdateProcessList response)
    {
        if (_processIdAndItems == null)
        {
            return;
        }

        foreach (var updateInfo in response.Processes ?? [])
        {
            if (_processIdAndItems.TryGetValue(updateInfo.Pid, out var process))
            {
                process.Status = (ProcessStatus)updateInfo.ProcessStatus;
                process.AlarmStatus = (AlarmStatus)updateInfo.AlarmStatus;
                process.Cpu = updateInfo.Cpu;
                process.Memory = updateInfo.Memory;
                process.Disk = updateInfo.Disk;
                process.Network = updateInfo.Network;
                process.Gpu = updateInfo.Gpu;
                process.PowerUsage = (PowerUsage)updateInfo.PowerUsage;
                process.UpdateTime = DateTime.Now;
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            DisplayProcesses.Clear();
            DisplayProcesses.AddRange(_receivedProcesses);
            this.RaisePropertyChanged(nameof(ProcessSummary));
        });
    }

    private void ReceivedSocketMessage(ChangeProcessList response)
    {
        Logger.Info("服务端通知进程集合发生变更。");
    }
}
