using CodeWF.EventBus;
using CodeWF.NetWrapper.Helpers;

namespace CodeWF.NetWrapper.Commands;

/// <summary>
/// TCP客户端连接异常通知命令
/// </summary>
public class TcpClientErrorCommand(TcpSocketClient client, string errorMessage) : Command
{
    /// <summary>
    /// TCP客户端对象
    /// </summary>
    public TcpSocketClient Client { get; private set; } = client;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; private set; } = errorMessage;
}