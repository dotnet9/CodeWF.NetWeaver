using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using System;
using System.Net;
using System.Net.Sockets;

namespace CodeWF.NetWrapper.Helpers;
public class UdpSocketServer
{
    private UdpClient? _client;
    private IPEndPoint? _udpIpEndPoint;


    #region 公开属性

    /// <summary>
    /// 服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }
    public string? ServerIP { get; private set; }
    public int ServerPort { get; private set; }
    public long SystemId { get; private set; }
    public static string LoopbackIP { get; set; } = "127.0.0.1";
    public static string LoopbackSubIP { get; set; } = "239.0.0.1";


    /// <summary>
    ///     是否正在运行udp组播订阅
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    ///     命令发送时间
    /// </summary>
    public DateTime SendTime { get; set; }


    /// <summary>
    ///     响应接收时间
    /// </summary>
    public DateTime ReceiveTime { get; set; }


    /// <summary>
    ///     Udp单包大小上限
    /// </summary>
    public int PacketMaxSize { get; set; } = 65507;

    #endregion

    #region 公开接口方法


    public (bool IsSuccess, string? ErrorMessage) Start(string serverMark, long systemId, string serverIP, int serverPort, string localIP)
    {
        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;
        SystemId = systemId;

        try
        {
            var localNic = IPAddress.Parse(localIP);
            _client = new UdpClient();

            // 允许复用端口（可选）
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // 绑定到指定网卡（非常关键）
            _client.Client.Bind(new IPEndPoint(localNic, 0));

            // 设置组播 TTL（本地网段）
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);

            // * 设置发送使用的网卡接口
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localNic.GetAddressBytes());

            // 开启回环
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            _udpIpEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);

            IsRunning = true;

            Logger.Info($"{ServerIP} 组播启动成功，组播地址：{ServerIP}:{ServerPort}");
            return (IsSuccess: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            Logger.Error($"{ServerIP} 组播启动失败，组播地址：{ServerIP}:{ServerPort}", ex, $"{ServerIP} 组播启动失败，组播地址：{ServerIP}:{ServerPort}，详细信息请查看日志文件");
            return (IsSuccess: false, ErrorMessage: $"{ServerIP} 组播启动失败，组播地址：{ServerIP}:{ServerPort}，异常信息：{ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            _client?.Close();
            _client = null;
            Logger.Info($"{ServerIP} 组播停止");
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerIP} 组播停止Udp异常",ex, $"{ServerIP} 组播停止Udp异常，详细信息请查看日志文件");
        }

        IsRunning = false;
    }

    public void SendCommand(INetObject command, DateTimeOffset time)
    {
        if (!IsRunning || _client == null) return;

        var buffer = command.Serialize(SystemId, time);
        var sendCount = _client.Send(buffer, buffer.Length, _udpIpEndPoint);
        if (sendCount < buffer.Length)
        {
            Console.WriteLine($"UDP发送失败一包：{buffer.Length}=>{sendCount}");
        }
    }

    #endregion
}