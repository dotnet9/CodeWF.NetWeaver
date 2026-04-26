using SocketTest.Client.Features.Processes.ViewModels;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ClientConnectionPanelViewModel
{
    public ClientConnectionPanelViewModel(ProcessMonitorViewModel processMonitorViewModel)
    {
        ProcessMonitorViewModel = processMonitorViewModel;
    }

    public ProcessMonitorViewModel ProcessMonitorViewModel { get; }
}
