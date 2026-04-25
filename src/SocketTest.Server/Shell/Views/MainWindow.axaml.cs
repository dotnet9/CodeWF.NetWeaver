using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SocketTest.Server.Shell.ViewModels;

namespace SocketTest.Server.Shell.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is not MainWindowViewModel { NotificationManager: null } viewModel)
        {
            return;
        }

        viewModel.NotificationManager = new WindowNotificationManager(GetTopLevel(this)) { MaxItems = 3 };
    }
}
