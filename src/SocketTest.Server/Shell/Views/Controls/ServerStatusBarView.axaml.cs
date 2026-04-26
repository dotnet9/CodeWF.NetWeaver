using Avalonia.Controls;
using SocketTest.Server.Shell.ViewModels;

namespace SocketTest.Server.Shell.Views.Controls;

public partial class ServerStatusBarView : UserControl
{
    public ServerStatusBarView()
    {
        InitializeComponent();
        DataContext = new ServerStatusBarViewModel();
    }
}
