using System;
using System.Net.Sockets;
using System.Threading;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// TCP 会话类，用于管理 TCP 连接会话信息
/// </summary>
public class TcpSession
{
    /// <summary>
    /// 获取或设置 TCP Socket 对象
    /// </summary>
    public Socket? TcpSocket { get; set; }
    
    /// <summary>
    /// 获取或设置取消令牌源，用于取消会话相关的异步操作
    /// </summary>
    public CancellationTokenSource? TokenSource { get; set; }
    
    /// <summary>
    /// 获取或设置会话最后活动时间
    /// </summary>
    public DateTime? ActiveTime { get; set; }
}