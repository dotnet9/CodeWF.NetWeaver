using Avalonia;
using Avalonia.Markup.Xaml;
using CodeWF.NetWrapper.Helpers;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Mvvm;
using SocketTest.Client.Features.Processes.Views;
using SocketTest.Client.Shell.Views;
using SocketTest.Client.Shell.Views.Controls;
using SocketTest.Client.Features.Processes.ViewModels;
using SocketTest.Client.Features.RemoteFiles.ViewModels;
using SocketTest.Client.Features.RemoteFiles.Views;
using SocketTest.Client.Features.Transfers.ViewModels;
using SocketTest.Client.Features.Transfers.Views;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client;

public class App : PrismApplication
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<TcpSocketClient>();
        containerRegistry.RegisterSingleton<UdpSocketClient>();

        containerRegistry.RegisterSingleton<ProcessMonitorViewModel>();
        containerRegistry.RegisterSingleton<FileTransferViewModel>();
        containerRegistry.RegisterSingleton<RemoteFileExplorerViewModel>();

        containerRegistry.RegisterSingleton<ClientConnectionPanelViewModel>();
        containerRegistry.RegisterSingleton<ClientModuleTabsViewModel>();
        containerRegistry.RegisterSingleton<ClientStatusBarViewModel>();
        containerRegistry.RegisterSingleton<ProcessMonitorStatusViewModel>();
        containerRegistry.RegisterSingleton<RemoteFileExplorerStatusViewModel>();
        containerRegistry.RegisterSingleton<FileTransferStatusViewModel>();

        containerRegistry.Register<MainWindow>();
    }

    protected override AvaloniaObject CreateShell() => Container.Resolve<MainWindow>();

    protected override void ConfigureViewModelLocator()
    {
        base.ConfigureViewModelLocator();
        ViewModelLocationProvider.Register<ClientConnectionPanelView, ClientConnectionPanelViewModel>();
        ViewModelLocationProvider.Register<ClientModuleTabsView, ClientModuleTabsViewModel>();
        ViewModelLocationProvider.Register<ClientStatusBarView, ClientStatusBarViewModel>();
        ViewModelLocationProvider.Register<ProcessMonitorStatusView, ProcessMonitorStatusViewModel>();
        ViewModelLocationProvider.Register<RemoteFileExplorerStatusView, RemoteFileExplorerStatusViewModel>();
        ViewModelLocationProvider.Register<FileTransferStatusView, FileTransferStatusViewModel>();
        ViewModelLocationProvider.Register<ProcessMonitorView, ProcessMonitorViewModel>();
        ViewModelLocationProvider.Register<RemoteFileExplorerView, RemoteFileExplorerViewModel>();
        ViewModelLocationProvider.Register<FileTransferView, FileTransferViewModel>();
    }
}
