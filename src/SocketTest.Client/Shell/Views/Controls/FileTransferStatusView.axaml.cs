using Avalonia.Controls;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Shell.Views.Controls;

public partial class FileTransferStatusView : UserControl
{
    public FileTransferStatusView()
    {
        InitializeComponent();
        DataContext = new FileTransferStatusViewModel();
    }
}
