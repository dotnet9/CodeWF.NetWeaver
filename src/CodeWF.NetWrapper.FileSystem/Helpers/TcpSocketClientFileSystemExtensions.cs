namespace CodeWF.NetWrapper.Helpers;

public static class TcpSocketClientFileSystemExtensions
{
    private static readonly ConditionalWeakTable<TcpSocketClient, TcpSocketClientFileSystemFeature> Features = new();

    public static TcpSocketClientFileSystemFeature UseFileSystem(this TcpSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Features.GetValue(client, static current => new TcpSocketClientFileSystemFeature(current));
    }

    public static Task BrowseFileSystemAsync(this TcpSocketClient client, string serverDirectoryPath,
        CancellationToken cancellationToken = default) =>
        client.UseFileSystem().BrowseFileSystemAsync(serverDirectoryPath, cancellationToken);

    public static Task CreateDirectoryAsync(this TcpSocketClient client, string serverDirectoryPath,
        CancellationToken cancellationToken = default) =>
        client.UseFileSystem().CreateDirectoryAsync(serverDirectoryPath, cancellationToken);

    public static Task DeletePathAsync(this TcpSocketClient client, string serverPath, bool isDirectory,
        CancellationToken cancellationToken = default) =>
        client.UseFileSystem().DeletePathAsync(serverPath, isDirectory, cancellationToken);

    public static Task UploadFileAsync(this TcpSocketClient client, string localFilePath, string remoteFilePath,
        CancellationToken cancellationToken = default) =>
        client.UseFileSystem().UploadFileAsync(localFilePath, remoteFilePath, cancellationToken);

    public static Task DownloadFileAsync(this TcpSocketClient client, string serverFilePath, string localSaveDirectory,
        CancellationToken cancellationToken = default) =>
        client.UseFileSystem().DownloadFileAsync(serverFilePath, localSaveDirectory, cancellationToken);
}
