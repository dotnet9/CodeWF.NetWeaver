using CodeWF.EventBus;
using ReactiveUI;
using SocketTest.Client.Features.Processes.ViewModels;
using SocketTest.Client.Features.RemoteFiles.ViewModels;
using SocketTest.Client.Features.Transfers.ViewModels;
using SocketTest.Client.Shell.Messages;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ClientModuleTabsViewModel : ReactiveObject
{
    public ClientModuleTabsViewModel(
        ProcessMonitorViewModel processMonitorViewModel,
        RemoteFileExplorerViewModel remoteFileExplorerViewModel,
        FileTransferViewModel fileTransferViewModel)
    {
        ProcessMonitorViewModel = processMonitorViewModel;
        RemoteFileExplorerViewModel = remoteFileExplorerViewModel;
        FileTransferViewModel = fileTransferViewModel;

        PublishSelectedTab();
    }

    public ProcessMonitorViewModel ProcessMonitorViewModel { get; }

    public RemoteFileExplorerViewModel RemoteFileExplorerViewModel { get; }

    public FileTransferViewModel FileTransferViewModel { get; }

    public int SelectedTabIndex
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            PublishSelectedTab();
        }
    }

    private void PublishSelectedTab() =>
        _ = EventBus.Default.PublishAsync(new ClientShellTabChangedMessage(SelectedTabIndex));
}
