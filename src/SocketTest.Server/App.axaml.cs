using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using CodeWF.NetWrapper.Helpers;
using SocketTest.Server.ViewModels;
using SocketTest.Server.Views;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SocketTest.Server;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.DataContext = new MainWindowViewModel();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void RegisterFileTransferExample(TcpSocketServer tcpHelper)
    {
        tcpHelper.FileTransferProgress += (sender, e) =>
        {
            var direction = e.IsUpload ? "上传" : "下载";
            Console.WriteLine($"[文件传输] {direction}进度：{e.FileName} - {e.Progress:F2}% ({e.TransferredBytes}/{e.TotalBytes}字节)");
        };
    }

    public static async Task ExampleFileUploadAsync(TcpSocketServer tcpHelper, string clientKey, string localFilePath)
    {
        var fileName = Path.GetFileName(localFilePath);
        await tcpHelper.StartFileUploadAsync(clientKey, localFilePath, fileName);
        Console.WriteLine($"[文件传输] 开始上传文件：{fileName}，目标客户端：{clientKey}");
    }

    public static async Task ExampleFileDownloadAsync(TcpSocketServer tcpHelper, string clientKey, string saveDirectory)
    {
        await tcpHelper.StartFileDownloadAsync(clientKey, saveDirectory);
        Console.WriteLine($"[文件传输] 开始下载文件到目录：{saveDirectory}");
    }
}