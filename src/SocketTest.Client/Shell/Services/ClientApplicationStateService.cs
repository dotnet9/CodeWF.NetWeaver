using ReactiveUI;

namespace SocketTest.Client.Shell.Services;

public sealed class ClientApplicationStateService : ReactiveObject
{
    public string ConnectionSummary { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "尚未连接到 TCP 服务端";

    public string UdpSummary { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "未建立 UDP 通道";

    public int TimestampStartYear { get; set => this.RaiseAndSetIfChanged(ref field, value); } = 2020;

    public string ProcessSummary { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "当前显示 0 个进程";

    public string CurrentDirectoryPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "/";

    public string ExplorerSummary { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "当前目录项 0 个";

    public string ExplorerStatusMessage { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "请先连接到服务端。";

    public string TransferQueueSummary { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "暂无传输任务";

    public string TransferSpeed { get; set => this.RaiseAndSetIfChanged(ref field, value); } = "0 B/s";

    public double TransferTotalProgress { get; set => this.RaiseAndSetIfChanged(ref field, value); }
}
