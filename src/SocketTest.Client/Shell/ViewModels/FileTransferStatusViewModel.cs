using SocketTest.Client.Features.Transfers.ViewModels;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class FileTransferStatusViewModel
{
    public FileTransferStatusViewModel(FileTransferViewModel fileTransferViewModel)
    {
        FileTransferViewModel = fileTransferViewModel;
    }

    public FileTransferViewModel FileTransferViewModel { get; }
}
