using Avalonia.Controls;
using Avalonia.Interactivity;
using SocketTest.Client.Models;
using SocketTest.Client.ViewModels;

namespace SocketTest.Client.Views;

public partial class FileTransferView : UserControl
{
    public FileTransferView()
    {
        InitializeComponent();
    }

    private void ToggleTransferMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileTransferViewModel vm)
        {
            return;
        }

        if (sender is MenuItem { DataContext: FileTransferItem item })
        {
            vm.ToggleTransfer(item);
        }
    }

    private void DeleteTransferMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileTransferViewModel vm)
        {
            return;
        }

        if (sender is MenuItem { DataContext: FileTransferItem item })
        {
            vm.RemoveTransferItem(item);
        }
    }
}
