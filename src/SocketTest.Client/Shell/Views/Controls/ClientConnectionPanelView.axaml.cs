using Avalonia.Controls;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Shell.Views.Controls;

public partial class ClientConnectionPanelView : UserControl
{
    public ClientConnectionPanelView()
    {
        InitializeComponent();
        DataContext = new ClientConnectionPanelViewModel();
    }
}
