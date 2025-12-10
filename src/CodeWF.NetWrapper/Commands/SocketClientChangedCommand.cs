using CodeWF.EventBus;
using CodeWF.NetWrapper.Helpers;

namespace CodeWF.NetWrapper.Commands;

/// <summary>
/// Socket 客户端变化命令，用于通知客户端连接状态变化
/// </summary>
public class SocketClientChangedCommand(TcpSocketServer server) : Command
{
    /// <summary>
    /// 获取或设置 TCP Socket 服务器对象
    /// </summary>
    public TcpSocketServer Server { get; set; } = server;
}