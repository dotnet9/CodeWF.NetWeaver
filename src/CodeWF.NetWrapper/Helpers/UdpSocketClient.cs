using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWrapper.Commands;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// UDP Socket 客户端类，用于与 UDP 服务端建立连接并进行通信。
/// </summary>
public class UdpSocketClient
{
    /// <summary>
    /// 接收缓冲区通道。
    /// </summary>
    private Channel<SocketCommand> _receivedBuffers = Channel.CreateUnbounded<SocketCommand>();

    /// <summary>
    /// UDP 客户端对象。
    /// </summary>
    private UdpClient? _client;

    #region 公开属性

    /// <summary>
    /// 服务标识，用于区分多个服务。
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    /// 服务端 IP 地址。
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    /// 服务端端口号。
    /// </summary>
    public int ServerPort { get; private set; }

    /// <summary>
    /// 是否正在运行 UDP 组播订阅。
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 新数据通知事件。
    /// </summary>
    public EventHandler<SocketCommand>? Received;

    /// <summary>
    /// 系统 ID，用于标识客户端身份。
    /// </summary>
    public long SystemId { get; private set; }

    #endregion

    #region 公开接口

    /// <summary>
    /// 连接到 UDP 服务端。
    /// </summary>
    /// <param name="serverMark">服务端标识。</param>
    /// <param name="serverIP">服务端 IP 地址。</param>
    /// <param name="serverPort">服务端端口号。</param>
    /// <param name="endpoint">本地 TCP 已连接端点。</param>
    /// <param name="systemId">客户端系统 ID。</param>
    public async Task ConnectAsync(string serverMark, string serverIP, int serverPort, string endpoint, long systemId)
    {
        if (_client != null || IsRunning)
        {
            Stop();
        }

        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;
        SystemId = systemId;

        try
        {
            var receivedBuffers = ResetReceivedBuffers();
            if (!IPAddress.TryParse(ServerIP, out var multicastAddress))
            {
                throw new InvalidOperationException($"无效的 UDP 组播地址：{ServerIP}");
            }

            var localAddress = ResolveLocalAddress(endpoint);
            _client = new UdpClient(multicastAddress.AddressFamily)
            {
                EnableBroadcast = true
            };
            _client.ExclusiveAddressUse = false;
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
            _client.Client.Bind(new IPEndPoint(localAddress, ServerPort));

            if (UdpSocketServer.LoopbackIP == ServerIP)
            {
                _client.JoinMulticastGroup(IPAddress.Parse(UdpSocketServer.LoopbackSubIP), localAddress);
            }
            else
            {
                _client.JoinMulticastGroup(multicastAddress, localAddress);
            }

            IsRunning = true;

            _ = Task.Run(async () => await ReceiveDataAsync(_client, receivedBuffers.Writer));
            _ = Task.Run(async () => await CheckCommandMeAsync(receivedBuffers.Reader));
        }
        catch (Exception ex)
        {
            CloseClient();
            CompleteReceivedBuffers();
            IsRunning = false;
            Logger.Error(
                $"{ServerMark} 连接异常",
                ex,
                uiContent: $"{ServerMark} 连接异常：{ex.Message}，详细信息请查看日志文件");
        }
    }

    /// <summary>
    /// 停止 UDP 客户端。
    /// </summary>
    /// <returns>是否成功停止。</returns>
    public bool Stop()
    {
        try
        {
            IsRunning = false;
            CompleteReceivedBuffers();
            CloseClient();
            Logger.Info($"{ServerMark} 停止");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 停止异常", ex, $"{ServerMark} 停止异常，详细信息请查看日志文件");
            IsRunning = false;
            return false;
        }
    }

    #endregion

    #region 接收处理数据

    /// <summary>
    /// 接收数据。
    /// </summary>
    private async Task ReceiveDataAsync(UdpClient client, ChannelWriter<SocketCommand> receivedBuffers)
    {
        while (IsRunning && ReferenceEquals(_client, client))
        {
            try
            {
                if (client.Client == null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                    continue;
                }

                var result = await client.ReceiveAsync();
                var data = result.Buffer;
                var readIndex = 0;
                if (!data.ReadHead(ref readIndex, out var headInfo)
                    || headInfo.SystemId != SystemId)
                {
                    continue;
                }

                if (data.Length < headInfo.BufferLen)
                {
                    Logger.Warn($"{ServerMark} 接收到不完整 UDP 包，接收大小 {data.Length}，错误 UDP 包基本信息：{headInfo}");
                    continue;
                }

                receivedBuffers.TryWrite(new SocketCommand(headInfo!, data));
            }
            catch (SocketException ex)
            {
                if (IsRunning && ReferenceEquals(_client, client))
                {
                    Logger.Error(ex.SocketErrorCode == SocketError.Interrupted
                        ? $"{ServerMark} Udp 中断，停止接收数据。"
                        : $"{ServerMark} 接收 Udp 数据异常：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                if (IsRunning && ReferenceEquals(_client, client))
                {
                    Logger.Error($"接收 Udp 数据异常：{ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 检查消息通道。
    /// </summary>
    private async Task CheckCommandMeAsync(ChannelReader<SocketCommand> receivedBuffers)
    {
        await foreach (var message in receivedBuffers.ReadAllAsync())
        {
            Received?.Invoke(this, message);
        }
    }

    private Channel<SocketCommand> ResetReceivedBuffers()
    {
        _receivedBuffers = Channel.CreateUnbounded<SocketCommand>();
        return _receivedBuffers;
    }

    private void CompleteReceivedBuffers()
    {
        _receivedBuffers.Writer.TryComplete();
    }

    private void CloseClient()
    {
        var client = _client;
        _client = null;
        client?.Close();
    }

    private static IPAddress ResolveLocalAddress(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return IPAddress.Any;
        }

        if (IPEndPoint.TryParse(endpoint, out var ipEndPoint))
        {
            return NormalizeLocalAddress(ipEndPoint.Address);
        }

        var separatorIndex = endpoint.LastIndexOf(':');
        var addressText = separatorIndex > 0
            ? endpoint[..separatorIndex]
            : endpoint;

        return IPAddress.TryParse(addressText, out var address)
            ? NormalizeLocalAddress(address)
            : IPAddress.Any;
    }

    private static IPAddress NormalizeLocalAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address)
            ? IPAddress.Any
            : address;
    }

    #endregion
}
