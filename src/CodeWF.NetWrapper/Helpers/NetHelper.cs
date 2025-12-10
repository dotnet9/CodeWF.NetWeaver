using CodeWF.Log.Core;
using System;
using System.Net.Sockets;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// 网络辅助类，提供网络通信相关的辅助方法
/// </summary>
public static class NetHelper
{
    /// <summary>
    /// 任务ID计数器
    /// </summary>
    private static int _taskId;
    
    /// <summary>
    /// 获取任务 ID，每次调用自增
    /// </summary>
    /// <returns>任务 ID</returns>
    public static int GetTaskId() => System.Threading.Interlocked.Increment(ref _taskId);

    /// <summary>
    /// 获取忽略任务的 ID
    /// </summary>
    /// <returns>忽略任务的 ID</returns>
    public static int GetIgnoreTask() => int.MinValue;

    /// <summary>
    /// 安全关闭 Socket 连接
    /// </summary>
    /// <param name="socket">要关闭的 Socket 对象</param>
    public static void CloseSocket(this Socket? socket)
    {
        try
        {
            // TODO这段代码不能使服务端收到客户端断开连接通知，暂时注释，后面解决
            //if (socket?.Connected == true)
            //{
            //    socket.Shutdown(SocketShutdown.Both);
            //}
        }
        catch (Exception ex)
        {
            Logger.Error($"关闭Socket连接（{socket?.RemoteEndPoint}）时发生异常",ex, $"关闭Socket连接（{socket?.RemoteEndPoint}）时发生异常，详细信息请查看日志文件");
        }
        finally
        {
            socket?.Close();
        }
    }
}