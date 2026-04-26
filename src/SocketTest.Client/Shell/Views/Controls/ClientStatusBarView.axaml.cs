using Avalonia.Controls;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Shell.Views.Controls;

public partial class ClientStatusBarView : UserControl
{
    public ClientStatusBarView()
    {
        InitializeComponent();
        DataContext = new ClientStatusBarViewModel();
    }
}
