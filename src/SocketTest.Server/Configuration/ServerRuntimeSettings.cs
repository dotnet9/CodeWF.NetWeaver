namespace SocketTest.Server.Configuration;

/// <summary>
/// 服务端运行时配置。
/// </summary>
public class ServerRuntimeSettings
{
    /// <summary>
    /// TCP 监听地址。
    /// </summary>
    public string TcpIp { get; set; } = "0.0.0.0";

    /// <summary>
    /// TCP 监听端口。
    /// </summary>
    public int TcpPort { get; set; }

    /// <summary>
    /// UDP 组播地址。
    /// </summary>
    public string UdpIp { get; set; } = "239.0.0.1";

    /// <summary>
    /// UDP 组播端口。
    /// </summary>
    public int UdpPort { get; set; }
}
