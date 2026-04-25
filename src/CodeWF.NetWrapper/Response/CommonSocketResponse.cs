using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Models;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 通用 Socket 响应对象。
/// </summary>
[NetHead(SocketConstants.CommonSocketResponseObjectId, 1)]
public class CommonSocketResponse : INetObject
{
    /// <summary>
    /// 请求任务 ID，用于把响应与原请求对应起来。
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 响应状态码。
    /// </summary>
    public byte Status { get; set; }

    /// <summary>
    /// 响应消息。
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 创建一个成功响应对象。
    /// </summary>
    /// <param name="taskId">请求任务 ID。</param>
    /// <param name="message">响应消息。</param>
    /// <returns>成功响应对象。</returns>
    public static CommonSocketResponse Success(int taskId, string message = "Success")
    {
        return new CommonSocketResponse
        {
            TaskId = taskId,
            Status = (byte)TcpResponseStatus.Success,
            Message = message
        };
    }

    /// <summary>
    /// 创建一个失败响应对象。
    /// </summary>
    /// <param name="taskId">请求任务 ID。</param>
    /// <param name="message">响应消息。</param>
    /// <returns>失败响应对象。</returns>
    public static CommonSocketResponse Fail(int taskId, string message = "Fail")
    {
        return new CommonSocketResponse
        {
            TaskId = taskId,
            Status = (byte)TcpResponseStatus.Fail,
            Message = message
        };
    }
}
