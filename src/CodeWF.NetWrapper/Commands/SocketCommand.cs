using CodeWF.EventBus;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using System.Net.Sockets;

namespace CodeWF.NetWrapper.Commands;

/// <summary>
/// Socket 命令类，用于封装 Socket 通信中的命令信息
/// </summary>
public class SocketCommand(NetHeadInfo netHeadInfo, byte[] buffer, Socket? client = null) : Command
{
    /// <summary>
    /// 获取客户端 Socket 对象
    /// </summary>
    public Socket? Client { get; } = client;

    /// <summary>
    /// 获取网络头信息
    /// </summary>
    public NetHeadInfo HeadInfo { get; } = netHeadInfo;

    /// <summary>
    /// 获取原始数据缓冲区
    /// </summary>
    private byte[] Buffer { get; } = buffer;

    /// <summary>
    /// 检查是否为指定类型的命令
    /// </summary>
    /// <typeparam name="T">命令类型</typeparam>
    /// <returns>是否为指定类型的命令</returns>
    public bool IsCommand<T>() => HeadInfo.IsNetObject<T>();

    /// <summary>
    /// 检查是否为指定类型的命令，但版本号不同
    /// </summary>
    /// <typeparam name="T">命令类型</typeparam>
    /// <returns>是否为指定类型的命令</returns>
    public bool IsCommandDiffVersion<T>() => HeadInfo.IsNetObjectDiffVersion<T>();

    /// <summary>
    /// 获取命令对象
    /// </summary>
    /// <typeparam name="T">命令类型</typeparam>
    /// <returns>命令对象</returns>
    public T GetCommand<T>() where T : new() => Buffer.Deserialize<T>();

    /// <summary>
    /// 返回命令的字符串表示
    /// </summary>
    /// <returns>命令的字符串表示</returns>
    public override string ToString() => $"{nameof(NetHeadInfo.ObjectId)}: {HeadInfo.ObjectId}";
}