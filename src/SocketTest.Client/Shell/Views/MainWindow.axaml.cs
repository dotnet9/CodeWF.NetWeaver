using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Shell.Views;

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
