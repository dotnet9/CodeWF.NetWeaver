using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SocketTest.Client.Models;
using SocketTest.Client.ViewModels;
using System.Linq;
using System.Threading.Tasks;

namespace SocketTest.Client.Views;

public partial class RemoteFileManagerView : UserControl
{
    public RemoteFileManagerView()
    {
        InitializeComponent();
    }

    private async void UploadFilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        var topLevel = TopLevel.GetTopLevel(this);
        if (vm == null || topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要上传的文件",
            AllowMultiple = true
        });

        if (files.Count == 0)
        {
            return;
        }

        await vm.UploadFilesAsync(files.Select(file => file.Path.LocalPath), GetContextItem(sender));
    }

    private async void UploadFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        var topLevel = TopLevel.GetTopLevel(this);
        if (vm == null || topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择要上传的文件夹"
        });

        if (folders.Count == 0)
        {
            return;
        }

        await vm.UploadFolderAsync(folders[0].Path.LocalPath, GetContextItem(sender));
    }

    private async void DownloadSelectionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await DownloadTargetAsync(sender);
    }

    private async void DownloadItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await DownloadTargetAsync(sender);
    }

    private async Task DownloadTargetAsync(object? sender)
    {
        var vm = GetViewModel();
        var topLevel = TopLevel.GetTopLevel(this);
        if (vm == null || topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择下载到本地的目录"
        });

        if (folders.Count == 0)
        {
            return;
        }

        await vm.DownloadItemAsync(GetContextItem(sender) ?? vm.SelectedServerItem, folders[0].Path.LocalPath);
    }

    private void CreateDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowCreateDirectoryDialogFor(GetContextItem(sender));
    }

    private void CreateDirectoryForItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowCreateDirectoryDialogFor(GetContextItem(sender));
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null)
        {
            return;
        }

        vm.ShowDeleteDialogFor(GetContextItem(sender) ?? vm.SelectedServerItem);
    }

    private void DeleteItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowDeleteDialogFor(GetContextItem(sender));
    }

    private async void OpenItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        var item = GetContextItem(sender);
        if (vm == null || item == null)
        {
            return;
        }

        await vm.OpenItemFromMenuAsync(item);
    }

    private void UploadFilesForItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        UploadFilesButton_OnClick(sender, e);
    }

    private void UploadFolderForItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        UploadFolderButton_OnClick(sender, e);
    }

    private RemoteFileManagerViewModel? GetViewModel() => DataContext as RemoteFileManagerViewModel;

    private static ServerDirectoryItem? GetContextItem(object? sender) =>
        (sender as MenuItem)?.DataContext as ServerDirectoryItem;
}
