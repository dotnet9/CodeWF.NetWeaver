using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CodeWF.NetWrapper.Helpers;
using SocketTest.Client.ViewModels;
using SocketTest.Client.Views;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SocketTest.Client;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

        base.OnFrameworkInitializationCompleted();
    }

    public static void RegisterFileTransferExample(TcpSocketClient tcpHelper)
    {
        tcpHelper.FileTransferProgress += (sender, e) =>
        {
            var direction = e.IsUpload ? "上传" : "下载";
            Console.WriteLine($"[文件传输] {direction}进度：{e.FileName} - {e.Progress:F2}% ({e.TransferredBytes}/{e.TotalBytes}字节)");
        };
    }

    public static async Task ExampleFileUploadAsync(TcpSocketClient tcpHelper, string localFilePath)
    {
        var fileName = Path.GetFileName(localFilePath);
        await tcpHelper.StartFileUploadAsync(localFilePath, fileName);
        Console.WriteLine($"[文件传输] 开始上传文件：{fileName}");
    }

    public static async Task ExampleFileDownloadAsync(TcpSocketClient tcpHelper, string serverFileName, string savePath)
    {
        await tcpHelper.StartFileDownloadAsync(serverFileName, savePath);
        Console.WriteLine($"[文件传输] 开始下载文件：{serverFileName}，保存路径：{savePath}");
    }
}