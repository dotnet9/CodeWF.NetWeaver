using CodeWF.EventBus;
using CodeWF.NetWrapper.Helpers;

namespace CodeWF.NetWrapper.Commands;

public class SocketClientChangedCommand(TcpSocketServer server) : Command
{
    public TcpSocketServer Server { get; set; } = server;
}