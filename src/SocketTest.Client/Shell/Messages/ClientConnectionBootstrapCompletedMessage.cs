using CodeWF.EventBus;

namespace SocketTest.Client.Shell.Messages;

public sealed class ClientConnectionBootstrapCompletedMessage(int timestampStartYear) : Command
{
    public int TimestampStartYear { get; } = timestampStartYear;
}
