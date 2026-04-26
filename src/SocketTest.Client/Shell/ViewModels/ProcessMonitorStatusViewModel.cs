using ReactiveUI;
using SocketTest.Client.Shell.Services;
using System.ComponentModel;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ProcessMonitorStatusViewModel : ReactiveObject
{
    private readonly ClientApplicationStateService _appState;

    public ProcessMonitorStatusViewModel(ClientApplicationStateService appState)
    {
        _appState = appState;
        _appState.PropertyChanged += HandleAppStatePropertyChanged;
    }

    public string ConnectionSummary => _appState.ConnectionSummary;

    public string UdpSummary => _appState.UdpSummary;

    public string ProcessSummary => _appState.ProcessSummary;

    private void HandleAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClientApplicationStateService.ConnectionSummary))
        {
            this.RaisePropertyChanged(nameof(ConnectionSummary));
            return;
        }

        if (e.PropertyName is nameof(ClientApplicationStateService.UdpSummary))
        {
            this.RaisePropertyChanged(nameof(UdpSummary));
            return;
        }

        if (e.PropertyName is nameof(ClientApplicationStateService.ProcessSummary))
        {
            this.RaisePropertyChanged(nameof(ProcessSummary));
        }
    }
}
