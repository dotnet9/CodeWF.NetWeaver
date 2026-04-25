namespace SocketTest.Client.Features.Transfers.Models;

public enum FileTransferState
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed
}
