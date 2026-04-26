using CodeWF.EventBus;
using System.Collections.Generic;

namespace SocketTest.Client.Features.Transfers.Messages;

public sealed class FileTransferEnqueueDownloadsMessage(IReadOnlyList<FileTransferPathPair> items) : Command
{
    public IReadOnlyList<FileTransferPathPair> Items { get; } = items;
}
