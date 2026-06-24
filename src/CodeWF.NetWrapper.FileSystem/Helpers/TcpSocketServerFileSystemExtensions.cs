namespace CodeWF.NetWrapper.Helpers;

public static class TcpSocketServerFileSystemExtensions
{
    private static readonly ConditionalWeakTable<TcpSocketServer, TcpSocketServerFileSystemFeature> Features = new();

    public static TcpSocketServerFileSystemFeature UseFileSystem(this TcpSocketServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return Features.GetValue(server, static current => new TcpSocketServerFileSystemFeature(current));
    }
}
