using Avalonia.Controls.Notifications;
using ReactiveUI;

namespace SocketTest.Client.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public MainWindowViewModel()
    {
        ProcessMonitorViewModel = new ProcessMonitorViewModel();
        FileTransferViewModel = new FileTransferViewModel(ProcessMonitorViewModel.TcpHelper);
        RemoteFileManagerViewModel = new RemoteFileManagerViewModel(ProcessMonitorViewModel.TcpHelper, FileTransferViewModel);

        ProcessMonitorViewModel.ConnectionStateChanged += isConnected =>
        {
            _ = RemoteFileManagerViewModel.HandleConnectionStateChangedAsync(isConnected);
        };
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public ProcessMonitorViewModel ProcessMonitorViewModel { get; }

    public FileTransferViewModel FileTransferViewModel { get; }

    public RemoteFileManagerViewModel RemoteFileManagerViewModel { get; }

    public int SelectedTabIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsProcessMonitorTabSelected));
            this.RaisePropertyChanged(nameof(IsRemoteFileManagerTabSelected));
            this.RaisePropertyChanged(nameof(IsFileTransferTabSelected));
        }
    }

    public bool IsProcessMonitorTabSelected => SelectedTabIndex == 0;

    public bool IsRemoteFileManagerTabSelected => SelectedTabIndex == 1;

    public bool IsFileTransferTabSelected => SelectedTabIndex == 2;
}
