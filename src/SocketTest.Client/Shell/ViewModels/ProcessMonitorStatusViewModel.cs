using ReactiveUI;
using SocketTest.Client.Features.Processes.ViewModels;
using System.ComponentModel;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ProcessMonitorStatusViewModel : ReactiveObject
{
    private readonly ProcessMonitorViewModel _processMonitorViewModel;

    public ProcessMonitorStatusViewModel(ProcessMonitorViewModel processMonitorViewModel)
    {
        _processMonitorViewModel = processMonitorViewModel;
        _processMonitorViewModel.PropertyChanged += HandleProcessMonitorPropertyChanged;
    }

    public string ConnectionSummary => _processMonitorViewModel.ConnectionSummary;

    public string UdpSummary => _processMonitorViewModel.UdpSummary;

    public string ProcessSummary => _processMonitorViewModel.ProcessSummary;

    private void HandleProcessMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProcessMonitorViewModel.ConnectionSummary))
        {
            this.RaisePropertyChanged(nameof(ConnectionSummary));
            return;
        }

        if (e.PropertyName is nameof(ProcessMonitorViewModel.UdpSummary))
        {
            this.RaisePropertyChanged(nameof(UdpSummary));
            return;
        }

        if (e.PropertyName is nameof(ProcessMonitorViewModel.ProcessSummary))
        {
            this.RaisePropertyChanged(nameof(ProcessSummary));
        }
    }
}
