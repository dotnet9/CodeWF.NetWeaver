using CodeWF.EventBus;
using ReactiveUI;
using SocketTest.Client.Shell.Messages;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ClientStatusBarViewModel : ReactiveObject
{
    public ClientStatusBarViewModel()
    {
        EventBus.Default.Subscribe(this);
        ApplySelectedTab(0);
    }

    public bool IsProcessMonitorTabSelected { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public bool IsRemoteFileExplorerTabSelected { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public bool IsFileTransferTabSelected { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    [EventHandler]
    private void ReceiveClientShellTabChanged(ClientShellTabChangedMessage message) =>
        ApplySelectedTab(message.SelectedTabIndex);

    private void ApplySelectedTab(int selectedTabIndex)
    {
        IsProcessMonitorTabSelected = selectedTabIndex == 0;
        IsRemoteFileExplorerTabSelected = selectedTabIndex == 1;
        IsFileTransferTabSelected = selectedTabIndex == 2;
    }
}
