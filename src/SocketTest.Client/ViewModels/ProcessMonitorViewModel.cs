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

namespace SocketTest.Client.ViewModels;

public class ProcessMonitorViewModel : ReactiveObject
{
    private readonly List<ProcessItemModel> _receivedProcesses = new();
    private int[]? _processIdArray;
    private Dictionary<int, ProcessItemModel>? _processIdAndItems;

    public ProcessMonitorViewModel()
    {
        DisplayProcesses = new();
        RegisterCommand();
        EventBus.Default.Subscribe(this);
        Logger.Info("进程监控初始化完成");
    }

    public RangObservableCollection<ProcessItemModel> DisplayProcesses { get; }

    public string? TcpIp { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; } = 5000;
    public string? UdpIp { get; set; } = "239.255.255.250";
    public int UdpPort { get; set; } = 11012;
    public long SystemId { get; set; } = 1000;

    public bool IsRunning => TcpHelper.IsRunning;
    public string BaseInfo => TcpHelper.IsRunning ? $"已连接" : "未连接";
    public string ConnectButtonText => TcpHelper.IsRunning ? "断开" : "连接";

    public TcpSocketClient TcpHelper { get; } = new();
    public UdpSocketClient UdpHelper { get; } = new();

    public ReactiveCommand<Unit, Unit> HandleConnectTcpCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> RefreshAllCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SendCorrectCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SendDiffVersionCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SendDiffPropsCommand { get; private set; } = null!;

    private void RegisterCommand()
    {
        HandleConnectTcpCommand = ReactiveCommand.Create(HandleConnectTcpCommandImpl);
        RefreshCommand = ReactiveCommand.Create(HandleRefreshCommand);
        RefreshAllCommand = ReactiveCommand.Create(HandleRefreshAllCommand);
        SendCorrectCommand = ReactiveCommand.Create(HandleSendCorrectCommand);
        SendDiffVersionCommand = ReactiveCommand.Create(HandleSendDiffVersionCommand);
        SendDiffPropsCommand = ReactiveCommand.Create(HandleSendDiffPropsCommand);
    }

    private void HandleConnectTcpCommandImpl()
    {
        if (TcpHelper.IsRunning)
        {
            TcpHelper.Stop();
        }
        else
        {
            _ = TcpHelper.ConnectAsync("TestClient", TcpIp!, TcpPort);
        }
        this.RaisePropertyChanged(nameof(IsRunning));
        this.RaisePropertyChanged(nameof(BaseInfo));
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
            Logger.Info($"正确连接{type.GetDescription()}，程序正常运行");
        }
    }

    private void ReceivedSocketMessage(ResponseUdpAddress response)
    {
        UdpIp = response.Ip;
        UdpPort = response.Port;
        Logger.Info($"UDP组播地址：{UdpIp}:{UdpPort}");
        _ = UdpHelper.ConnectAsync("Server", UdpIp!, UdpPort, string.Empty, SystemId);
    }

    private void ReceivedSocketMessage(ResponseServiceInfo response)
    {
        Logger.Info($"收到服务信息：{response.OS}，系统时间戳起始年份：{response.TimestampStartYear}");
    }

    private void ReceivedSocketMessage(ResponseProcessIDList response)
    {
        _processIdArray = response.IDList;
        _processIdAndItems = new Dictionary<int, ProcessItemModel>();
        foreach (var process in _receivedProcesses)
        {
            _processIdAndItems[process.PID] = process;
        }
    }

    private void ReceivedSocketMessage(ResponseProcessList response)
    {
        _receivedProcesses.Clear();
        foreach (var process in response.Processes)
        {
            var item = new ProcessItemModel(process, 2020);
            _receivedProcesses.Add(item);
        }

        if (_processIdArray != null)
        {
            foreach (var pid in _processIdArray)
            {
                var process = _receivedProcesses.FirstOrDefault(p => p.PID == pid);
                if (process != null)
                {
                    _processIdAndItems?.Add(pid, process);
                }
            }
        }
    }

    private void ReceivedSocketMessage(UpdateProcessList response)
    {
        foreach (var updateInfo in response.Processes)
        {
            if (_processIdAndItems != null && _processIdAndItems.TryGetValue(updateInfo.Pid, out var process))
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

        DisplayProcesses.Clear();
        DisplayProcesses.AddRange(_receivedProcesses);
        this.RaisePropertyChanged(nameof(BaseInfo));
    }

    private void ReceivedSocketMessage(ChangeProcessList response)
    {
    }
}