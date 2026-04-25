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
    public async Task UploadFileAsync_UploadsFileIntoServerRoot()
    {
        var serverRoot = CreateDirectory("server-upload-root");
        var localRoot = CreateDirectory("client-upload-root");
        var localFile = Path.Combine(localRoot, "upload.bin");
        await File.WriteAllBytesAsync(localFile, CreateTestBytes(180_000));

        await using var harness = await CreateHarnessAsync(serverRoot);

        await harness.Client.UploadFileAsync(localFile, "uploads/upload.bin");

        var serverFile = Path.Combine(serverRoot, "uploads", "upload.bin");
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

        await using var harness = await CreateHarnessAsync(serverRoot);

        await harness.Client.DownloadFileAsync("downloads/server.bin", localRoot);

        var localFile = Path.Combine(localRoot, "server.bin");
        await WaitForConditionAsync(() => File.Exists(localFile) &&
                                         new FileInfo(localFile).Length == new FileInfo(serverFile).Length);

        Assert.Equal(await ComputeHashAsync(serverFile), await ComputeHashAsync(localFile));
    }

    [Fact]
    public async Task FileSaveDirectory_RestrictsOperationsToManagedRoot()
    {
        var serverRoot = CreateDirectory("server-managed-root");
        var outsideRoot = CreateDirectory("outside-root");
        var outsideFile = Path.Combine(outsideRoot, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "do-not-delete");

        await using var harness = await CreateHarnessAsync(serverRoot);

        await harness.Client.CreateDirectoryAsync("managed");
        await WaitForConditionAsync(() => Directory.Exists(Path.Combine(serverRoot, "managed")));

        await harness.Client.DeleteDirectoryAsync("managed");
        await WaitForConditionAsync(() => !Directory.Exists(Path.Combine(serverRoot, "managed")));

        await harness.Client.DeleteFileAsync(@"..\outside-root\outside.txt");
        await Task.Delay(500);

        Assert.True(File.Exists(outsideFile));
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_workspaceRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<TestHarness> CreateHarnessAsync(string serverRoot)
    {
        var port = GetFreePort();
        var server = new TcpSocketServer
        {
            FileSaveDirectory = serverRoot
        };
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
