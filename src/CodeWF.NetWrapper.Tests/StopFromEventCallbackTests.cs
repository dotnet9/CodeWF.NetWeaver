using CodeWF.EventBus;
using DefaultEventBus = CodeWF.EventBus.EventBus;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;

namespace CodeWF.NetWrapper.Tests;

/// <summary>
///     回归"订阅者在库事件回调里调用 Stop() 导致自死锁"的缺陷：
///     库若在内部接收/回调线程上同步派发事件，订阅者在回调里调用 Stop() 时
///     Stop() 会等待内部线程退出，而该线程正阻塞在派发本次事件上，形成自己等自己的死锁。
///     断言整个流程（含回调里的 Stop()）在 5 秒内完成。
/// </summary>
[Collection(LoggingCollection.Name)]
public sealed class StopFromEventCallbackTests
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task TcpClient_StopFromDisconnectCallback_CompletesWithinTimeout()
    {
        var port = GetFreePort();
        var server = new TcpSocketServer();
        var client = new TcpSocketClient();
        var stopCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new DisconnectStopSubscriber(client, stopCompleted);

        var serverResult = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(serverResult.IsSuccess, serverResult.ErrorMessage);

        DefaultEventBus.Default.Subscribe(subscriber);
        try
        {
            var clientResult = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
            Assert.True(clientResult.IsSuccess, clientResult.ErrorMessage);
            await WaitForConditionAsync(() => server.Clients.Count == 1);

            // 下游真实场景：服务端正常停止，主动关闭 TCP 连接
            var serverStop = server.StopAsync();

            var finished = await Task.WhenAny(stopCompleted.Task, Task.Delay(StopTimeout));
            Assert.True(ReferenceEquals(finished, stopCompleted.Task),
                "在断开事件回调里调用 Stop() 未在 5 秒内完成，疑似死锁");
            Assert.False(client.IsRunning);

            await serverStop;
        }
        finally
        {
            DefaultEventBus.Default.Unsubscribe(subscriber);
            await StopTcpClientWithTimeoutAsync(client);
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task TcpClient_StopFromReceivedCallback_CompletesWithinTimeout()
    {
        var port = GetFreePort();
        var server = new TcpSocketServer();
        var client = new TcpSocketClient();
        var stopCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new ReceivedStopSubscriber(client, stopCompleted);

        var serverResult = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(serverResult.IsSuccess, serverResult.ErrorMessage);

        DefaultEventBus.Default.Subscribe(subscriber);
        try
        {
            var clientResult = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
            Assert.True(clientResult.IsSuccess, clientResult.ErrorMessage);
            await WaitForConditionAsync(() => server.Clients.Count == 1);

            await server.SendCommandAsync(new TestNotification { Seq = 1 });

            var finished = await Task.WhenAny(stopCompleted.Task, Task.Delay(StopTimeout));
            Assert.True(ReferenceEquals(finished, stopCompleted.Task),
                "在收包事件回调里调用 Stop() 未在 5 秒内完成，疑似死锁");
            Assert.False(client.IsRunning);
        }
        finally
        {
            DefaultEventBus.Default.Unsubscribe(subscriber);
            await StopTcpClientWithTimeoutAsync(client);
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task UdpClient_StopFromReceivedCallback_CompletesWithinTimeout()
    {
        var port = GetFreeUdpPort();
        var client = new UdpSocketClient();
        await client.ConnectAsync("UdpTestClient", UdpSocketServer.LoopbackIP, port, "127.0.0.1:50000", 1);
        await WaitForConditionAsync(() => client.IsRunning);

        var stopCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Received += (sender, message) =>
        {
            if (client.IsRunning)
            {
                client.Stop();
            }

            stopCompleted.TrySetResult();
        };

        var server = new UdpSocketServer();
        var startResult = server.Start("UdpTestServer", 1, UdpSocketServer.LoopbackSubIP, port,
            IPAddress.Loopback.ToString());
        Assert.True(startResult.IsSuccess, startResult.ErrorMessage);

        try
        {
            // 组播偶发丢包，持续发送直到回调完成
            var sendTask = Task.Run(async () =>
            {
                for (var i = 0; i < 20 && !stopCompleted.Task.IsCompleted; i++)
                {
                    await server.SendCommandAsync(new Heartbeat { TaskId = 100 + i }, DateTimeOffset.UtcNow);
                    await Task.Delay(200);
                }
            });

            var finished = await Task.WhenAny(stopCompleted.Task, Task.Delay(StopTimeout));
            Assert.True(ReferenceEquals(finished, stopCompleted.Task),
                "在 UDP 收包回调里调用 Stop() 未在 5 秒内完成，疑似死锁");
            Assert.False(client.IsRunning);
            await sendTask;
        }
        finally
        {
            server.Stop();
            StopUdpClientWithTimeout(client);
        }
    }

    [Fact]
    public async Task TcpClient_AfterStopFromCallback_CanCreateNewClientAndConnect()
    {
        var firstPort = GetFreePort();
        var firstServer = new TcpSocketServer();
        var firstClient = new TcpSocketClient();
        var stopCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new DisconnectStopSubscriber(firstClient, stopCompleted);

        var firstServerResult = await firstServer.StartAsync("TestServer1", IPAddress.Loopback.ToString(), firstPort);
        Assert.True(firstServerResult.IsSuccess, firstServerResult.ErrorMessage);

        DefaultEventBus.Default.Subscribe(subscriber);
        TcpSocketServer? secondServer = null;
        TcpSocketClient? secondClient = null;
        try
        {
            var firstClientResult =
                await firstClient.ConnectAsync("TestClient1", IPAddress.Loopback.ToString(), firstPort);
            Assert.True(firstClientResult.IsSuccess, firstClientResult.ErrorMessage);
            await WaitForConditionAsync(() => firstServer.Clients.Count == 1);

            await firstServer.StopAsync();

            var finished = await Task.WhenAny(stopCompleted.Task, Task.Delay(StopTimeout));
            Assert.True(ReferenceEquals(finished, stopCompleted.Task),
                "在断开事件回调里调用 Stop() 未在 5 秒内完成，疑似死锁");
            Assert.False(firstClient.IsRunning);

            // 回调里 Stop() 之后，库仍可创建新客户端并正常连接、收发
            var secondPort = GetFreePort();
            secondServer = new TcpSocketServer();
            var secondServerResult =
                await secondServer.StartAsync("TestServer2", IPAddress.Loopback.ToString(), secondPort);
            Assert.True(secondServerResult.IsSuccess, secondServerResult.ErrorMessage);

            secondClient = new TcpSocketClient();
            var secondClientResult =
                await secondClient.ConnectAsync("TestClient2", IPAddress.Loopback.ToString(), secondPort);
            Assert.True(secondClientResult.IsSuccess, secondClientResult.ErrorMessage);
            await WaitForConditionAsync(() => secondServer.Clients.Count == 1 && secondClient.CanSend);

            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var normalSubscriber = new ReceivedCollector(received);
            DefaultEventBus.Default.Subscribe(normalSubscriber);
            try
            {
                await secondServer.SendCommandAsync(new TestNotification { Seq = 2 });

                var receivedFinished = await Task.WhenAny(received.Task, Task.Delay(StopTimeout));
                Assert.True(ReferenceEquals(receivedFinished, received.Task),
                    "回调里 Stop() 后，新客户端未能在 5 秒内收到服务端命令，疑似资源残留或串扰");
                Assert.True(secondClient.IsRunning);
            }
            finally
            {
                DefaultEventBus.Default.Unsubscribe(normalSubscriber);
            }
        }
        finally
        {
            DefaultEventBus.Default.Unsubscribe(subscriber);
            await StopTcpClientWithTimeoutAsync(firstClient);
            if (secondClient != null)
            {
                await StopTcpClientWithTimeoutAsync(secondClient);
            }

            if (secondServer != null)
            {
                await secondServer.StopAsync();
            }
        }
    }

    private static async Task StopTcpClientWithTimeoutAsync(TcpSocketClient client)
    {
        try
        {
            // 修复前死锁场景下 Stop() 无法返回，这里兜底避免测试主机挂死
            await Task.Run(client.Stop).WaitAsync(CleanupTimeout);
        }
        catch (TimeoutException)
        {
        }
    }

    private static void StopUdpClientWithTimeout(UdpSocketClient client)
    {
        try
        {
            Task.Run(client.Stop).Wait(CleanupTimeout);
        }
        catch (Exception)
        {
            // 修复前死锁场景下 Stop() 无法返回，这里兜底避免测试主机挂死
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutSeconds = 8)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "条件在超时时间内未满足");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetFreeUdpPort()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    [NetHead(65003, 1)]
    private sealed class TestNotification : INetObject
    {
        public int Seq { get; set; }
    }

    private sealed class DisconnectStopSubscriber(TcpSocketClient client, TaskCompletionSource stopCompleted)
    {
        [EventHandler]
        private void OnClientError(TcpClientErrorCommand message)
        {
            if (!ReferenceEquals(message.Client, client))
            {
                return;
            }

            client.Stop();
            stopCompleted.TrySetResult();
        }
    }

    private sealed class ReceivedStopSubscriber(TcpSocketClient client, TaskCompletionSource stopCompleted)
    {
        [EventHandler]
        private void OnSocketCommand(SocketCommand message)
        {
            if (!message.IsCommand<TestNotification>() || client.IsRunning == false)
            {
                return;
            }

            client.Stop();
            stopCompleted.TrySetResult();
        }
    }

    private sealed class ReceivedCollector(TaskCompletionSource received)
    {
        [EventHandler]
        private void OnSocketCommand(SocketCommand message)
        {
            if (message.IsCommand<TestNotification>())
            {
                received.TrySetResult();
            }
        }
    }
}
