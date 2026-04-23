using Avalonia.Controls.Notifications;
using ReactiveUI;

namespace SocketTest.Client.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    public MainWindowViewModel()
    {
        ProcessMonitorViewModel = new ProcessMonitorViewModel();
        FileTransferViewModel = new FileTransferViewModel();
        RemoteFileManagerViewModel = new RemoteFileManagerViewModel();
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public ProcessMonitorViewModel ProcessMonitorViewModel { get; }
    public FileTransferViewModel FileTransferViewModel { get; }
    public RemoteFileManagerViewModel RemoteFileManagerViewModel { get; }
}