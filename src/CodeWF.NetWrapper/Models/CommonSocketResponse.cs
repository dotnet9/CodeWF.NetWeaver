using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 通用 Socket 响应类，用于封装通用的 Socket 响应信息
/// </summary>
[NetHead(SocketConstants.CommonSocketResponseObjectId, 1)]
public class CommonSocketResponse : INetObject
{
    /// <summary>
    /// 获取或设置任务 ID
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 获取或设置响应状态
    /// </summary>
    public byte Status { get; set; }

    /// <summary>
    /// 获取或设置响应消息
    /// </summary>
    public string? Message { get; set; }

    public static CommonSocketResponse Success(int taskId, string message = "Success")
    {
        return new CommonSocketResponse()
        {
            TaskId = taskId,
            Status = (byte)TcpResponseStatus.Success,
            Message = message
        };
    }

    public static CommonSocketResponse Fail(int taskId, string message = "Fail")
    {
        return new CommonSocketResponse()
        {
            TaskId = taskId,
            Status = (byte)TcpResponseStatus.Fail,
            Message = message
        };
    }
}