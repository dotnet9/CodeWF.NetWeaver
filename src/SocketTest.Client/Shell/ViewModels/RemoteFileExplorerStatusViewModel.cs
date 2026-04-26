using ReactiveUI;
using SocketTest.Client.Shell.Services;
using System.ComponentModel;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class RemoteFileExplorerStatusViewModel : ReactiveObject
{
    private readonly ClientApplicationStateService _appState;

    public RemoteFileExplorerStatusViewModel(ClientApplicationStateService appState)
    {
        _appState = appState;
        _appState.PropertyChanged += HandleAppStatePropertyChanged;
    }

    public string CurrentDirectoryPath => _appState.CurrentDirectoryPath;

    public string ExplorerSummary => _appState.ExplorerSummary;

    public string StatusMessage => _appState.ExplorerStatusMessage;

    private void HandleAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClientApplicationStateService.CurrentDirectoryPath))
        {
            this.RaisePropertyChanged(nameof(CurrentDirectoryPath));
            return;
        }

        if (e.PropertyName is nameof(ClientApplicationStateService.ExplorerSummary))
        {
            this.RaisePropertyChanged(nameof(ExplorerSummary));
            return;
        }

        if (e.PropertyName is nameof(ClientApplicationStateService.ExplorerStatusMessage))
        {
            this.RaisePropertyChanged(nameof(StatusMessage));
        }
    }
}
