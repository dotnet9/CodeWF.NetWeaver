using Avalonia.Controls;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Shell.Views.Controls;

public partial class ClientModuleTabsView : UserControl
{
    public ClientModuleTabsView()
    {
        InitializeComponent();
        DataContext = new ClientModuleTabsViewModel();
    }
}
