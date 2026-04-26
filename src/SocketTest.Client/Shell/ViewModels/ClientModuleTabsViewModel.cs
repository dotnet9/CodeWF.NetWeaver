using CodeWF.EventBus;
using ReactiveUI;
using SocketTest.Client.Shell.Messages;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class ClientModuleTabsViewModel : ReactiveObject
{
    public ClientModuleTabsViewModel() => PublishSelectedTab();

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
