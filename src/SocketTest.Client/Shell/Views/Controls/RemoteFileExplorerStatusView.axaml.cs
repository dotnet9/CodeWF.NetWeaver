using Avalonia.Controls;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Shell.Views.Controls;

public partial class RemoteFileExplorerStatusView : UserControl
{
    public RemoteFileExplorerStatusView()
    {
        InitializeComponent();
        DataContext = new RemoteFileExplorerStatusViewModel();
    }
}
