using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWrapper.Commands;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// UDP Socket 客户端类，用于与 UDP 服务器建立连接并进行通信
/// </summary>
public class UdpSocketClient
{
    /// <summary>
    /// 接收缓冲区队列
    /// </summary>
    private readonly BlockingCollection<SocketCommand> _receivedBuffers = new(new ConcurrentQueue<SocketCommand>());

    /// <summary>
    /// UDP客户端对象
    /// </summary>
    private UdpClient? _client;

    /// <summary>
    /// 远程端点
    /// </summary>
    private IPEndPoint _remoteEp = new(IPAddress.Any, 0);

    #region 公开属性

    /// <summary>
    /// 服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    /// 获取或设置服务器IP地址
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    /// 获取或设置服务器端口号
    /// </summary>
    public int ServerPort { get; private set; }


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
    /// 新数据通知事件
    /// </summary>
    public Action<SocketCommand>? NewDataResponse;

    #endregion

    #region 公开接口

    /// <summary>
    /// 连接到UDP服务器
    /// </summary>
    /// <param name="serverMark">服务器标识</param>
    /// <param name="serverIP">服务器IP地址</param>
    /// <param name="serverPort">服务器端口号</param>
    /// <param name="endpoint">本地端点</param>
    public async Task ConnectAsync(string serverMark, string serverIP, int serverPort, string endpoint)
    {
        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;

        try
        {
            _client = new UdpClient();
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

            // 开启组播回环
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            var localIp = endpoint.Split(";").First();

            // 任意IP+广播端口，0是任意端口
            _client.Client.Bind(new IPEndPoint(IPAddress.Parse(localIp), ServerPort));

            if (UdpSocketServer.LoopbackIP == ServerIP)
            {
                _client.JoinMulticastGroup(IPAddress.Parse(UdpSocketServer.LoopbackSubIP), IPAddress.Parse(localIp!));
            }
            else
            {
                _client.JoinMulticastGroup(IPAddress.Parse(ServerIP), IPAddress.Parse(localIp!));
            }

            IsRunning = true;

            // 不再使用await，让方法在后台运行
            _ = ReceiveDataAsync();
            _ = CheckMessageAsync();
        }
        catch (Exception ex)
        {
            IsRunning = false;
            Logger.Error($"{ServerMark} 连接异常", ex, uiContent: $"{ServerMark} 连接异常：{ex.Message}，详细信息请查看日志文件");
        }
    }

    /// <summary>
    /// 停止UDP客户端
    /// </summary>
    /// <returns>是否成功停止</returns>
    public bool Stop()
    {
        try
        {
            _client?.Close();
            _client = null;
            Logger.Info($"{ServerMark} 停止");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 停止异常", ex, $"{ServerMark} 停止异常，详细信息请查看日志文件");
        }

        IsRunning = false;
        return false;
    }

    #endregion

    #region 接收处理数据

    /// <summary>
    /// 接收数据
    /// </summary>
    private async Task ReceiveDataAsync()
    {
        await Task.Run(async () =>
        {
            while (IsRunning)
                try
                {
                    if (_client?.Client == null || _client.Available < 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                        continue;
                    }

                    var data = _client.Receive(ref _remoteEp);
                    var readIndex = 0;

                    if (!data.ReadHead(ref readIndex, out var headInfo)
                        || data.Length < headInfo?.BufferLen)
                    {
                        Logger.Warn($"{ServerMark} 接收到不完整UDP包，接收大小 {data.Length}，错误UDP包基本信息：{headInfo}");
                        continue;
                    }

                    _receivedBuffers.Add(new SocketCommand(headInfo!, data));
                    ReceiveTime = DateTime.Now;
                }
                catch (SocketException ex)
                {
                    Logger.Error(ex.SocketErrorCode == SocketError.Interrupted
                        ? $"{ServerMark} Udp中断，停止接收数据！"
                        : $"{ServerMark} 接收Udp数据异常：{ex.Message}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"接收Udp数据异常：{ex.Message}");
                }
        });
    }

    /// <summary>
    /// 检查消息队列
    /// </summary>
    private async Task CheckMessageAsync()
    {
        await Task.Run(async () =>
        {
            while (!IsRunning)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            while (IsRunning)
            {
                while (_receivedBuffers.TryTake(out var message, TimeSpan.FromMilliseconds(10)))
                {
                    NewDataResponse?.Invoke(message);
                }
            }
        });
    }

    #endregion
}