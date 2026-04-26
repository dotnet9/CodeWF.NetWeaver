using ReactiveUI;
using SocketTest.Client.Features.Transfers.ViewModels;
using System.ComponentModel;

namespace SocketTest.Client.Shell.ViewModels;

public sealed class FileTransferStatusViewModel : ReactiveObject
{
    private readonly FileTransferViewModel _fileTransferViewModel;

    public FileTransferStatusViewModel(FileTransferViewModel fileTransferViewModel)
    {
        _fileTransferViewModel = fileTransferViewModel;
        _fileTransferViewModel.PropertyChanged += HandleFileTransferPropertyChanged;
    }

    public string QueueSummary => _fileTransferViewModel.QueueSummary;

    public string TransferSpeed => _fileTransferViewModel.TransferSpeed;

    public double TotalProgress => _fileTransferViewModel.TotalProgress;

    private void HandleFileTransferPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileTransferViewModel.QueueSummary))
        {
            this.RaisePropertyChanged(nameof(QueueSummary));
            return;
        }

        if (e.PropertyName is nameof(FileTransferViewModel.TransferSpeed))
        {
            this.RaisePropertyChanged(nameof(TransferSpeed));
            return;
        }

        if (e.PropertyName is nameof(FileTransferViewModel.TotalProgress))
        {
            this.RaisePropertyChanged(nameof(TotalProgress));
        }
    }
}
