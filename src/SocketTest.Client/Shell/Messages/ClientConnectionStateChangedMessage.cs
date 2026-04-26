using CodeWF.EventBus;

namespace SocketTest.Client.Shell.Messages;

public sealed class ClientConnectionStateChangedMessage(bool isConnected) : Command
{
    public bool IsConnected { get; } = isConnected;
}
