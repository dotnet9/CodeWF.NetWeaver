using SocketTest.Client.Features.Processes.ViewModels;
using SocketTest.Client.Features.RemoteFiles.ViewModels;
using SocketTest.Client.Features.Transfers.ViewModels;

namespace SocketTest.Client.Shell.ViewModels;

internal static class ClientShellViewModelRegistry
{
    static ClientShellViewModelRegistry()
    {
        ProcessMonitorViewModel = new ProcessMonitorViewModel();
        FileTransferViewModel = new FileTransferViewModel(ProcessMonitorViewModel.TcpHelper);
        RemoteFileExplorerViewModel = new RemoteFileExplorerViewModel(
            ProcessMonitorViewModel.TcpHelper,
            FileTransferViewModel);
    }

    public static ProcessMonitorViewModel ProcessMonitorViewModel { get; }

    public static FileTransferViewModel FileTransferViewModel { get; }

    public static RemoteFileExplorerViewModel RemoteFileExplorerViewModel { get; }
}
