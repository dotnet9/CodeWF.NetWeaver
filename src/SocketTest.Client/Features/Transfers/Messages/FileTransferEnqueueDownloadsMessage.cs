using System.Collections.Generic;
using CodeWF.EventBus;

namespace SocketTest.Client.Features.Transfers.Messages;

public sealed class FileTransferEnqueueDownloadsMessage(IReadOnlyList<FileTransferPathPair> items) : Command
{
    public IReadOnlyList<FileTransferPathPair> Items { get; } = items;
}
