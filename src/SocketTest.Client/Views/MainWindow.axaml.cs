using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using SocketTest.Client.ViewModels;
using SocketTest.Client.Models;

namespace SocketTest.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var vm = DataContext as MainWindowViewModel;
        if (vm is not { NotificationManager: null }) return;
        var topLevel = GetTopLevel(this);
        vm.NotificationManager =
            new WindowNotificationManager(topLevel) { MaxItems = 3 };
    }

    private void OnServerDirectoryItemDoubleTapped(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm?.SelectedServerItem is ServerDirectoryItem item && item.IsDirectory)
        {
            vm.EnterDirectoryCommand.Execute(item.Name);
        }
    }
}