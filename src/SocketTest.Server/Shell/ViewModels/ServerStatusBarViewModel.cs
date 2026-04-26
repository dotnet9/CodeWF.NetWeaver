using CodeWF.EventBus;
using ReactiveUI;
using SocketTest.Server.Shell.Messages;

namespace SocketTest.Server.Shell.ViewModels;

public sealed class ServerStatusBarViewModel : ReactiveObject
{
    public ServerStatusBarViewModel()
    {
        EventBus.Default.Subscribe(this);
    }

    public string ServiceStatusText { get; private set => this.RaiseAndSetIfChanged(ref field, value); } = "服务未启动";

    public int CurrentProcessCount { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    public int ClientCount { get; private set => this.RaiseAndSetIfChanged(ref field, value); }

    [EventHandler]
    private void ReceiveServerShellStatusChanged(ServerShellStatusChangedMessage message)
    {
        ServiceStatusText = message.ServiceStatusText;
        CurrentProcessCount = message.CurrentProcessCount;
        ClientCount = message.ClientCount;
    }
}
