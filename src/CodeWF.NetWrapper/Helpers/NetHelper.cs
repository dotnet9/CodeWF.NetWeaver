using CodeWF.Log.Core;
using System;
using System.Net.Sockets;

namespace CodeWF.NetWrapper.Helpers;
public static class NetHelper
{
    private static int _taskId;
    public static int GetTaskId() => System.Threading.Interlocked.Increment(ref _taskId);

    public static int GetIgnoreTask() => int.MinValue;

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