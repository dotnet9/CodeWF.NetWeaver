using CodeWF.EventBus;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using System.Net.Sockets;

namespace CodeWF.NetWrapper.Commands;

public class SocketCommand(NetHeadInfo netHeadInfo, byte[] buffer, Socket? client = null) : Command
{
    public Socket? Client { get; } = client;

    public NetHeadInfo HeadInfo { get;  } = netHeadInfo;

    private byte[] Buffer { get; } = buffer;

    public bool IsCommand<T>() => HeadInfo.IsNetObject<T>();

    public T GetCommand<T>() where T : new () => Buffer.Deserialize<T>();

    public override string ToString() => $"{nameof(NetHeadInfo.ObjectId)}: {HeadInfo.ObjectId}";
}