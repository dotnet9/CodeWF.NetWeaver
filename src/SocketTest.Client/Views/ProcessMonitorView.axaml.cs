using Avalonia.Controls;
using Avalonia.Interactivity;
using SocketTest.Client.Models;
using SocketTest.Client.ViewModels;

namespace SocketTest.Client.Views;

public partial class ProcessMonitorView : UserControl
{
    public ProcessMonitorView()
    {
        InitializeComponent();
    }

    private void TerminateProcessMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as ProcessMonitorViewModel;
        var process = (sender as MenuItem)?.DataContext as ProcessItemModel;
        vm?.ShowTerminateProcessDialog(process);
    }
}
