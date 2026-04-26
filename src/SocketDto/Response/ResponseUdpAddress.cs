namespace SocketDto.Response;

/// <summary>
///     响应Udp组播地址
/// </summary>
[NetHead(NetConsts.ResponseUdpAddressObjectId, 1)]
public class ResponseUdpAddress : INetObject
{
    /// <summary>
    ///     任务Id
    /// </summary>
    public int TaskId { get; set; }


    /// <summary>
    ///     组播地址
    /// </summary>
    public string? Ip { get; set; }

    /// <summary>
    ///     组播端口
    /// </summary>
    public int Port { get; set; }
    public override string ToString() => $"返回UDP地址(TaskId={TaskId},Ip={Ip},端口={Port})";
}
