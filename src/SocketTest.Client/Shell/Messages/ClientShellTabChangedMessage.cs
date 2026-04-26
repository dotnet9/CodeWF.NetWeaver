using CodeWF.EventBus;

namespace SocketTest.Client.Shell.Messages;

public sealed class ClientShellTabChangedMessage(int selectedTabIndex) : Command
{
    public int SelectedTabIndex { get; } = selectedTabIndex;
}
