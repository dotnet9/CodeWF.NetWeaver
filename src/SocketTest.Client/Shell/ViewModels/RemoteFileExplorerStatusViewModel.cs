using ReactiveUI;
using SocketTest.Client.Features.RemoteFiles.ViewModels;
using System.ComponentModel;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class RemoteFileExplorerStatusViewModel : ReactiveObject
{
    private readonly RemoteFileExplorerViewModel _remoteFileExplorerViewModel = ClientShellViewModelRegistry.RemoteFileExplorerViewModel;

    public RemoteFileExplorerStatusViewModel()
    {
        _remoteFileExplorerViewModel.PropertyChanged += HandleRemoteFileExplorerPropertyChanged;
    }

    public string CurrentDirectoryPath => _remoteFileExplorerViewModel.CurrentDirectoryPath;

    public string ExplorerSummary => _remoteFileExplorerViewModel.ExplorerSummary;

    public string StatusMessage => _remoteFileExplorerViewModel.StatusMessage;

    private void HandleRemoteFileExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RemoteFileExplorerViewModel.CurrentDirectoryPath))
        {
            this.RaisePropertyChanged(nameof(CurrentDirectoryPath));
            return;
        }

        if (e.PropertyName is nameof(RemoteFileExplorerViewModel.ExplorerSummary))
        {
            this.RaisePropertyChanged(nameof(ExplorerSummary));
            return;
        }

        if (e.PropertyName is nameof(RemoteFileExplorerViewModel.StatusMessage))
        {
            this.RaisePropertyChanged(nameof(StatusMessage));
        }
    }
}
