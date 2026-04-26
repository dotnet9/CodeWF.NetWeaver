using Avalonia.Controls.Notifications;
using SocketTest.Client.Features.Processes.ViewModels;
using SocketTest.Client.Features.RemoteFiles.ViewModels;
using SocketTest.Client.Features.Transfers.ViewModels;

namespace SocketTest.Client.Shell.ViewModels;

public class MainWindowViewModel
{
    public MainWindowViewModel()
    {
        var processMonitorViewModel = new ProcessMonitorViewModel();
        var fileTransferViewModel = new FileTransferViewModel(processMonitorViewModel.TcpHelper);
        var remoteFileExplorerViewModel = new RemoteFileExplorerViewModel(processMonitorViewModel.TcpHelper, fileTransferViewModel);

        ConnectionPanelViewModel = new ClientConnectionPanelViewModel(processMonitorViewModel);
        ModuleTabsViewModel = new ClientModuleTabsViewModel(
            processMonitorViewModel,
            remoteFileExplorerViewModel,
            fileTransferViewModel);
        StatusBarViewModel = new ClientStatusBarViewModel(
            new ProcessMonitorStatusViewModel(processMonitorViewModel),
            new RemoteFileExplorerStatusViewModel(remoteFileExplorerViewModel),
            new FileTransferStatusViewModel(fileTransferViewModel));
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public ClientConnectionPanelViewModel ConnectionPanelViewModel { get; }

    public ClientModuleTabsViewModel ModuleTabsViewModel { get; }

    public ClientStatusBarViewModel StatusBarViewModel { get; }
}
