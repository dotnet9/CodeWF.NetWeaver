using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// TCP Socket服务端
/// </summary>
public class TcpSocketServer
{
    public readonly ConcurrentDictionary<string, TcpSession> _clients = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<SocketCommand>> _requests = new();
    private PeriodicTimer? _detectionTimer;

    private CancellationTokenSource? _listenTokenSource;

    #region 公开属性

    public Socket? Server { get; private set; }
    public long SystemId { get; } // 服务端标识，TCP数据接收时保存，用于UDP数据包识别

    /// <summary>
    /// 服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    public string? ServerIP { get; private set; }
    public int ServerPort { get; private set; }

    /// <summary>
    /// 客户端心跳超时时间，单位秒
    /// </summary>
    public int TimeOut { get; private set; }

    /// <summary>
    ///     是否正在运行Tcp服务
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
    ///     心跳时间
    /// </summary>
    public DateTime HeartbeatTime { get; set; }

    #endregion

    #region 公开接口方法

    /// <summary>
    /// 启动服务
    /// </summary>
    /// <param name="serverMark">服务标识</param>
    /// <param name="serverIP">服务IP</param>
    /// <param name="serverPort">服务端口</param>
    /// <param name="timeout">客户端心跳超时</param>
    /// <returns></returns>
    public async Task<(bool IsSuccess, string? ErrorMessage)> StartAsync(string serverMark, string serverIP,
        int serverPort, int timeout = 30)
    {
        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;
        TimeOut = timeout;

        try
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            Server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            Server.Bind(ipEndPoint);
            Server.Listen(10);
            IsRunning = true;
            Logger.Info($"{ServerMark} 启动成功，服务地址是：{ServerIP}:{ServerPort}");

            _listenTokenSource = new CancellationTokenSource();

            await ListenForClientsAsync();
            await ProcessingRequestsAsync();

            _detectionTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            await DetectionClientsAsync();

            return (IsSuccess: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 启动失败，服务地址是：{ServerIP}:{ServerPort}", ex,
                uiContent: $"{ServerMark} 启动失败，服务地址是：{ServerIP}:{ServerPort}，详细日志查看日志文件");
            return (IsSuccess: false, ErrorMessage: $"{ServerMark} 启动失败，异常信息：{ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        IsRunning = false;
        _listenTokenSource?.Cancel();
        _detectionTimer?.Dispose();
        if(!_clients.IsEmpty)
        {
            var clientKeys = _clients.Keys.ToList();
            foreach(var clientKey in clientKeys)
            {
                await RemoveClientAsync(clientKey);
            }
        }
        Server?.Close(0);
        Server = null;
    }

    public async Task SendCommandAsync(INetObject command)
    {
        if (_clients.IsEmpty)
        {
            Logger.Debug($"{ServerMark} 没有客户端上线，无发送目的地址，无法发送命令");
            return;
        }

        var buffer = command.Serialize(SystemId);
        for(var i = _clients.Values.Count - 1; i >= 0; i--)
        {
            var client = _clients.Values.ElementAt(i);
            var clientKey = client.TcpSocket?.RemoteEndPoint?.ToString() ?? string.Empty;
            try
            {
                await SendCommandAsync(client.TcpSocket, command);
            }
            catch(SocketException ex)
            {
                Logger.Error($"{ServerMark} 发送命令到客户端({clientKey})异常，将移除该客户端", ex, uiContent: $"{ServerMark} 发送命令到客户端({clientKey})异常，将移除该客户端，详细信息请查看日志文件");
                await RemoveClientAsync(clientKey);
            }
        }
    }

    public async Task SendCommandAsync(Socket client, INetObject command)
    {
        var buffer = command.Serialize(SystemId);
        await client.SendAsync(buffer); 
    }

    private static int _taskId;

    public static int GetNewTaskId()
    {
        return ++_taskId;
    }

    #endregion

    #region 接收客户端命令

    private async Task RemoveClientAsync(Socket tcpClient)
    {
        await RemoveClientAsync(tcpClient.RemoteEndPoint!.ToString()!);
    }

    private async Task RemoveClientAsync(string key)
    {
        if (!_clients.TryGetValue(key, out var session))
        {
            return;
        }
        session.TokenSource?.Cancel();
        session.TcpSocket?.Close();

        _clients.TryRemove(key, out _);

        await EventBus.EventBus.Default.PublishAsync(new SocketClientChangedCommand(this));
        _requests.TryRemove(key, out _);
        Logger.Warn($"已清除客户端信息{key}");
    }

    private async Task ListenForClientsAsync()
    {
        while (IsRunning && _listenTokenSource?.IsCancellationRequested != true)
        {
            try
            {
                var socketClient = await Server!.AcceptAsync();
                var session = await CacheClientAsync(socketClient);
                await EventBus.EventBus.Default.PublishAsync(new SocketClientChangedCommand(this));

                var socketClientKey = $"{socketClient.RemoteEndPoint}";
                
                Logger.Info($"{ServerMark} 客户端({socketClientKey})连接上线");

                HandleClient(session);
            }
            catch (Exception ex)
            {
                if (IsRunning)
                {
                    Logger.Error($"{ServerMark} 处理客户端连接上线异常", ex, uiContent: $"{ServerMark} 处理客户端连接上线异常，详细信息请查看日志文件");
                }
            }
        }
    }

    private void HandleClient(TcpSession client)
    {
        Task.Run(async() =>
        {
            while (IsRunning && _listenTokenSource?.IsCancellationRequested != true &&
                   client.TokenSource?.IsCancellationRequested != true)
            {
                var tcpClientKey = string.Empty;
                try
                {
                    tcpClientKey = client.TcpSocket?.RemoteEndPoint?.ToString() ?? string.Empty;
                    while (client.TcpSocket?.ReadPacket(out var buffer, out var headInfo) == true)
                    {
                        if (!_requests.TryGetValue(tcpClientKey, out var value))
                        {
                            value = new ConcurrentQueue<SocketCommand>();
                            _requests[tcpClientKey] = value;
                        }

                        value.Enqueue(new SocketCommand(headInfo!, buffer, client.TcpSocket));
                    }
                }
                catch (SocketException ex)
                {
                    Logger.Error($"{ServerMark} 远程主机({tcpClientKey})异常，将移除该客户端", ex, uiContent: $"{ServerMark} 远程主机({tcpClientKey})异常，将移除该客户端，详细信息请查看日志文件");
                    await RemoveClientAsync(tcpClientKey);
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ServerMark} 接收数据异常", ex, uiContent: $"{ServerMark} 接收数据异常，详细信息请查看日志文件");
                }
            }

            return Task.CompletedTask;
        });
    }

    #endregion

    #region 处理客户端请求

    private async Task ProcessingRequestsAsync()
    {
        while (IsRunning && _listenTokenSource?.IsCancellationRequested != true)
        {
            try
            {
                var needRemoveKeys = new List<string>();
                foreach (var request in _requests)
                {
                    var clientKey = request.Key;
                    if (!_clients.TryGetValue(clientKey, out var client))
                    {
                        needRemoveKeys.Add(clientKey);
                        continue;
                    }

                    while (request.Value.TryDequeue(out var command))
                    {
                        await EventBus.EventBus.Default.PublishAsync(command);

                        if (command.IsCommand<Heartbeat>())
                        {
                            ActiveClient(clientKey);
                        }
                    }
                }

                if (needRemoveKeys.Count > 0)
                {
                    foreach (var key in needRemoveKeys)
                    {
                        await RemoveClientAsync(key);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 处理客户端请求异常", ex, uiContent: $"{ServerMark} 处理客户端请求异常，详细信息请查看日志文件");
            }
            finally
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
        }
    }

    private async Task<TcpSession> CacheClientAsync(Socket? socket)
    {
        var key = socket?.RemoteEndPoint?.ToString() ?? string.Empty;
        if (_clients.ContainsKey(key))
        {
            await RemoveClientAsync(key);
        }

        var session = new TcpSession
        {
            TcpSocket = socket,
            TokenSource = new CancellationTokenSource(),
            ActiveTime = DateTime.Now
        };
        _clients.TryAdd(key, session);
        return session;
    }

    private async Task DetectionClientsAsync()
    {
        if (_detectionTimer == null || _listenTokenSource == null)
            return;

        try
        {
            while (await _detectionTimer.WaitForNextTickAsync(_listenTokenSource.Token))
            {
                var clientKeys = _clients.Keys;
                foreach (var clientKey in clientKeys) 
                {
                    if(!_clients.TryGetValue(clientKey, out var clientSession))
                    {
                        continue;
                    }
                    if(!clientSession.ActiveTime.HasValue || DateTime.Now.Subtract(clientSession.ActiveTime.Value).TotalSeconds > TimeOut)
                    {
                        await RemoveClientAsync(clientKey);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 心跳检测异常", ex, uiContent: $"{ServerMark} 客户端心跳检测异常，详细信息请查看日志文件");
        }
    }

    private void ActiveClient(string clientKey)
    {
        if(_clients.TryGetValue(clientKey, out var session))
        {
            session.ActiveTime = DateTime.Now;
        }
    }

    #endregion
}