using SocketTest.Client.Features.RemoteFiles.ViewModels;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class RemoteFileExplorerStatusViewModel
{
    public RemoteFileExplorerStatusViewModel(RemoteFileExplorerViewModel remoteFileExplorerViewModel)
    {
        RemoteFileExplorerViewModel = remoteFileExplorerViewModel;
    }

    public RemoteFileExplorerViewModel RemoteFileExplorerViewModel { get; }
}
