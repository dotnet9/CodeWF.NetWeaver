using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CodeWF.NetWrapper.Helpers;
using Xunit;

namespace CodeWF.NetWrapper.Tests;

public sealed class TcpSocketFileTransferTests : IAsyncLifetime
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "CodeWF.NetWrapper.Tests", Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspaceRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UploadFileAsync_UploadsFileIntoRequestedServerPath()
    {
        var serverRoot = CreateDirectory("server-upload-root");
        var localRoot = CreateDirectory("client-upload-root");
        var localFile = Path.Combine(localRoot, "upload.bin");
        var serverFile = Path.Combine(serverRoot, "uploads", "upload.bin");
        await File.WriteAllBytesAsync(localFile, CreateTestBytes(180_000));

        await using var harness = await CreateHarnessAsync();

        await harness.Client.UploadFileAsync(localFile, serverFile);

        await WaitForConditionAsync(() => File.Exists(serverFile) &&
                                         new FileInfo(serverFile).Length == new FileInfo(localFile).Length);

        Assert.Equal(await ComputeHashAsync(localFile), await ComputeHashAsync(serverFile));
    }

    [Fact]
    public async Task DownloadFileAsync_SavesFileToRequestedLocalDirectory()
    {
        var serverRoot = CreateDirectory("server-download-root");
        var localRoot = CreateDirectory("client-download-root");
        var serverFile = Path.Combine(serverRoot, "downloads", "server.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(serverFile)!);
        await File.WriteAllBytesAsync(serverFile, CreateTestBytes(220_000));

        await using var harness = await CreateHarnessAsync();

        await harness.Client.DownloadFileAsync(serverFile, localRoot);

        var localFile = Path.Combine(localRoot, "server.bin");
        await WaitForConditionAsync(() => File.Exists(localFile) &&
                                         new FileInfo(localFile).Length == new FileInfo(serverFile).Length);

        Assert.Equal(await ComputeHashAsync(serverFile), await ComputeHashAsync(localFile));
    }

    [Fact]
    public async Task FileOperations_UseClientProvidedAbsolutePaths()
    {
        var serverRoot = CreateDirectory("server-managed-root");
        var managedDirectory = Path.Combine(serverRoot, "managed");
        var managedFile = Path.Combine(managedDirectory, "managed.txt");
        Directory.CreateDirectory(managedDirectory);
        await File.WriteAllTextAsync(managedFile, "delete-me");

        await using var harness = await CreateHarnessAsync();

        await harness.Client.CreateDirectoryAsync(managedDirectory);
        await WaitForConditionAsync(() => Directory.Exists(managedDirectory));

        await harness.Client.DeletePathAsync(managedFile, false);
        await WaitForConditionAsync(() => !File.Exists(managedFile));

        await harness.Client.DeletePathAsync(managedDirectory, true);
        await WaitForConditionAsync(() => !Directory.Exists(managedDirectory));
    }

    [Fact]
    public async Task Stop_RemovesClientFromServer()
    {
        var port = GetFreePort();
        var server = new TcpSocketServer();
        var client = new TcpSocketClient();

        var serverResult = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(serverResult.IsSuccess, serverResult.ErrorMessage);

        try
        {
            var clientResult = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
            Assert.True(clientResult.IsSuccess, clientResult.ErrorMessage);

            await WaitForConditionAsync(() => server.Clients.Count == 1);

            client.Stop();

            await WaitForConditionAsync(() => server.Clients.IsEmpty);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ServerStop_MarksClientAsDisconnected()
    {
        var port = GetFreePort();
        var server = new TcpSocketServer();
        var client = new TcpSocketClient();

        var serverResult = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(serverResult.IsSuccess, serverResult.ErrorMessage);

        try
        {
            var clientResult = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
            Assert.True(clientResult.IsSuccess, clientResult.ErrorMessage);

            await WaitForConditionAsync(() => server.Clients.Count == 1);

            await server.StopAsync();

            await WaitForConditionAsync(() => !client.IsRunning && client.LocalEndPoint == null && !client.CanSend);
        }
        finally
        {
            client.Stop();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Stop_AllowsClientToReconnect()
    {
        var port = GetFreePort();
        var server = new TcpSocketServer();
        var client = new TcpSocketClient();

        var serverResult = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(serverResult.IsSuccess, serverResult.ErrorMessage);

        try
        {
            var firstConnect = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
            Assert.True(firstConnect.IsSuccess, firstConnect.ErrorMessage);

            await WaitForConditionAsync(() => server.Clients.Count == 1);

            client.Stop();
            await WaitForConditionAsync(() => server.Clients.IsEmpty);

            var secondConnect = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
            Assert.True(secondConnect.IsSuccess, secondConnect.ErrorMessage);

            await WaitForConditionAsync(() => server.Clients.Count == 1 && client.CanSend);
        }
        finally
        {
            client.Stop();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task StopAsync_AllowsServerInstanceToRestart()
    {
        var port = GetFreePort();
        var server = new TcpSocketServer();
        var client = new TcpSocketClient();

        var firstStart = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(firstStart.IsSuccess, firstStart.ErrorMessage);
        await server.StopAsync();

        var secondStart = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(secondStart.IsSuccess, secondStart.ErrorMessage);

        try
        {
            var clientResult = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
            Assert.True(clientResult.IsSuccess, clientResult.ErrorMessage);

            await WaitForConditionAsync(() => server.Clients.Count == 1);
        }
        finally
        {
            client.Stop();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task UdpSocketClient_StopMarksStoppedAndAllowsReconnect()
    {
        var port = GetFreePort();
        var client = new UdpSocketClient();

        await client.ConnectAsync("UdpTestClient", UdpSocketServer.LoopbackIP, port, "127.0.0.1:50000", 1);
        await WaitForConditionAsync(() => client.IsRunning);

        Assert.True(client.Stop());
        Assert.False(client.IsRunning);

        await client.ConnectAsync("UdpTestClient", UdpSocketServer.LoopbackIP, port, "127.0.0.1:50000", 1);
        await WaitForConditionAsync(() => client.IsRunning);

        Assert.True(client.Stop());
        Assert.False(client.IsRunning);
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_workspaceRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<TestHarness> CreateHarnessAsync()
    {
        var port = GetFreePort();
        var server = new TcpSocketServer();
        var client = new TcpSocketClient();

        var serverResult = await server.StartAsync("TestServer", IPAddress.Loopback.ToString(), port);
        Assert.True(serverResult.IsSuccess, serverResult.ErrorMessage);

        var clientResult = await client.ConnectAsync("TestClient", IPAddress.Loopback.ToString(), port);
        Assert.True(clientResult.IsSuccess, clientResult.ErrorMessage);

        return new TestHarness(server, client);
    }

    private static byte[] CreateTestBytes(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 8000, int pollMs = 100)
    {
        var stopAt = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < stopAt)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(pollMs);
        }

        Assert.True(condition(), "Condition was not satisfied before timeout.");
    }

    private static async Task<string> ComputeHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    private sealed class TestHarness(TcpSocketServer server, TcpSocketClient client) : IAsyncDisposable
    {
        public TcpSocketServer Server { get; } = server;
        public TcpSocketClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Stop();
            await Server.StopAsync();
        }
    }
}
