using ReactiveUI;

namespace SocketTest.Client.Models;

public class FileTransferItem : ReactiveObject
{
    private double _progress;
    private long _transferredBytes;
    private string _commandText = "取消";
    private string _status = "等待";

    public string LocalPath { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public string TransferType { get; set; } = string.Empty;

    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    public long TransferredBytes
    {
        get => _transferredBytes;
        set => this.RaiseAndSetIfChanged(ref _transferredBytes, value);
    }

    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string CommandText
    {
        get => _commandText;
        set => this.RaiseAndSetIfChanged(ref _commandText, value);
    }
}