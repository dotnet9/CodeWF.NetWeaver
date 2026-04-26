using ReactiveUI;
using SocketTest.Client.Shell.Services;
using System.ComponentModel;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class FileTransferStatusViewModel : ReactiveObject
{
    private readonly ClientApplicationStateService _appState;

    public FileTransferStatusViewModel(ClientApplicationStateService appState)
    {
        _appState = appState;
        _appState.PropertyChanged += HandleAppStatePropertyChanged;
    }

    public string QueueSummary => _appState.TransferQueueSummary;

    public string TransferSpeed => _appState.TransferSpeed;

    public double TotalProgress => _appState.TransferTotalProgress;

    private void HandleAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClientApplicationStateService.TransferQueueSummary))
        {
            this.RaisePropertyChanged(nameof(QueueSummary));
            return;
        }

        if (e.PropertyName is nameof(ClientApplicationStateService.TransferSpeed))
        {
            this.RaisePropertyChanged(nameof(TransferSpeed));
            return;
        }

        if (e.PropertyName is nameof(ClientApplicationStateService.TransferTotalProgress))
        {
            this.RaisePropertyChanged(nameof(TotalProgress));
        }
    }
}
