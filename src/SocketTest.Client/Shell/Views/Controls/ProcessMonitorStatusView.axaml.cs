using Avalonia.Controls;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Shell.Views.Controls;

public partial class ProcessMonitorStatusView : UserControl
{
    public ProcessMonitorStatusView()
    {
        InitializeComponent();
        DataContext = new ProcessMonitorStatusViewModel();
    }
}
