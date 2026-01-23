using CodeWF.EventBus;
using CodeWF.NetWrapper.Helpers;

namespace CodeWF.NetWrapper.Commands;

/// <summary>
/// TCP客户端连接异常通知命令
/// </summary>
public class TcpClientErrorCommand(TcpSocketClient client, string errorMessage) : Command
{
    public TcpSocketClient Client { get; private set; } = client;
    public string ErrorMessage { get; private set; } = errorMessage;
}