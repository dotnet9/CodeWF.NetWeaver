using ReactiveUI;
using SocketTest.Client.Features.Processes.ViewModels;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ClientConnectionPanelViewModel : ReactiveObject
{
    private readonly ProcessMonitorViewModel _processMonitorViewModel = ClientShellViewModelRegistry.ProcessMonitorViewModel;

    public ClientConnectionPanelViewModel()
    {
        _processMonitorViewModel.PropertyChanged += HandleProcessMonitorPropertyChanged;
    }

    public string TcpIp
    {
        get => _processMonitorViewModel.TcpIp;
        set => _processMonitorViewModel.TcpIp = value;
    }

    public int TcpPort
    {
        get => _processMonitorViewModel.TcpPort;
        set => _processMonitorViewModel.TcpPort = value;
    }

    public string ConnectButtonText => _processMonitorViewModel.ConnectButtonText;

    public string ConnectionSummary => _processMonitorViewModel.ConnectionSummary;

    public Task HandleConnectTcpAsync() => _processMonitorViewModel.HandleConnectTcpAsync();

    private void HandleProcessMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProcessMonitorViewModel.TcpIp))
        {
            this.RaisePropertyChanged(nameof(TcpIp));
            return;
        }

        if (e.PropertyName is nameof(ProcessMonitorViewModel.TcpPort))
        {
            this.RaisePropertyChanged(nameof(TcpPort));
            return;
        }

        if (e.PropertyName is nameof(ProcessMonitorViewModel.ConnectButtonText))
        {
            this.RaisePropertyChanged(nameof(ConnectButtonText));
            return;
        }

        if (e.PropertyName is nameof(ProcessMonitorViewModel.ConnectionSummary))
        {
            this.RaisePropertyChanged(nameof(ConnectionSummary));
        }
    }
}
