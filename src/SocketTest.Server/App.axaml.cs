using Avalonia;
using Avalonia.Markup.Xaml;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Mvvm;
using SocketTest.Server.Features.Processes.Services;
using SocketTest.Server.Shell.Views;
using SocketTest.Server.Shell.Views.Controls;
using SocketTest.Server.Shell.ViewModels;

namespace SocketTest.Server;

public class App : PrismApplication
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        var processSnapshotProvider = ProcessSnapshotProviderFactory.CreateDefault();
        containerRegistry.RegisterInstance<IProcessSnapshotProvider>(processSnapshotProvider);
        containerRegistry.RegisterInstance(new MainWindowViewModel(processSnapshotProvider));
        containerRegistry.RegisterSingleton<ServerStatusBarViewModel>();
        containerRegistry.Register<MainWindow>();
    }

    protected override AvaloniaObject CreateShell() => Container.Resolve<MainWindow>();

    protected override void ConfigureViewModelLocator()
    {
        base.ConfigureViewModelLocator();
        ViewModelLocationProvider.Register<MainWindow, MainWindowViewModel>();
        ViewModelLocationProvider.Register<ServerStatusBarView, ServerStatusBarViewModel>();
    }
}
