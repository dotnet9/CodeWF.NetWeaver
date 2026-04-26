using Avalonia.Controls;
using Avalonia.Interactivity;
using SocketTest.Client.Features.Processes.Models;
using SocketTest.Client.Features.Processes.ViewModels;
using SocketTest.Client.Shell.ViewModels;

namespace SocketTest.Client.Features.Processes.Views;

public partial class ProcessMonitorView : UserControl
{
    public ProcessMonitorView()
    {
        InitializeComponent();
        DataContext = ClientShellViewModelRegistry.ProcessMonitorViewModel;
    }

    private void TerminateProcessMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as ProcessMonitorViewModel;
        var process = (sender as MenuItem)?.DataContext as ProcessItemModel;
        vm?.ShowTerminateProcessDialog(process);
    }
}
