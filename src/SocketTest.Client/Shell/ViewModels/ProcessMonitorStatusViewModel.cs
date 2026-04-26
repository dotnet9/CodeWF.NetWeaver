using SocketTest.Client.Features.Processes.ViewModels;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ProcessMonitorStatusViewModel
{
    public ProcessMonitorStatusViewModel(ProcessMonitorViewModel processMonitorViewModel)
    {
        ProcessMonitorViewModel = processMonitorViewModel;
    }

    public ProcessMonitorViewModel ProcessMonitorViewModel { get; }
}
