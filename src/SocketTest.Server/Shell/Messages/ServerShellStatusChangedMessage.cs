using CodeWF.EventBus;

namespace SocketTest.Server.Shell.Messages;

public sealed class ServerShellStatusChangedMessage(string serviceStatusText, int currentProcessCount, int clientCount) : Command
{
    public string ServiceStatusText { get; } = serviceStatusText;

    public int CurrentProcessCount { get; } = currentProcessCount;

    public int ClientCount { get; } = clientCount;
}
