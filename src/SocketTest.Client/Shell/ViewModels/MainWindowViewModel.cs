using Avalonia.Controls.Notifications;
using ReactiveUI;
using SocketTest.Client.Features.Processes.ViewModels;
using SocketTest.Client.Features.RemoteFiles.ViewModels;
using SocketTest.Client.Features.Transfers.ViewModels;

namespace SocketTest.Client.Shell.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public MainWindowViewModel()
    {
        ProcessMonitorViewModel = new ProcessMonitorViewModel();
        FileTransferViewModel = new FileTransferViewModel(ProcessMonitorViewModel.TcpHelper);
        RemoteFileExplorerViewModel = new RemoteFileExplorerViewModel(ProcessMonitorViewModel.TcpHelper, FileTransferViewModel);

        ProcessMonitorViewModel.ConnectionStateChanged += isConnected =>
        {
            _ = RemoteFileExplorerViewModel.HandleConnectionStateChangedAsync(isConnected);
        };
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public ProcessMonitorViewModel ProcessMonitorViewModel { get; }

    public FileTransferViewModel FileTransferViewModel { get; }

    public RemoteFileExplorerViewModel RemoteFileExplorerViewModel { get; }

    public int SelectedTabIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsProcessMonitorTabSelected));
            this.RaisePropertyChanged(nameof(IsRemoteFileExplorerTabSelected));
            this.RaisePropertyChanged(nameof(IsFileTransferTabSelected));
        }
    }

    public bool IsProcessMonitorTabSelected => SelectedTabIndex == 0;

    public bool IsRemoteFileExplorerTabSelected => SelectedTabIndex == 1;

    public bool IsFileTransferTabSelected => SelectedTabIndex == 2;
}
