using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SocketTest.Client.Models;
using SocketTest.Client.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SocketTest.Client.Views;

public partial class RemoteFileManagerView : UserControl
{
    public RemoteFileManagerView()
    {
        InitializeComponent();
    }

    private async void NavigationTree_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var vm = GetViewModel();
        var node = e.AddedItems.OfType<RemoteTreeNode>().FirstOrDefault();
        if (vm == null || node == null)
        {
            return;
        }

        await vm.SelectTreeNodeAsync(node);
    }

    private async void CurrentDirectoryTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var vm = GetViewModel();
        if (vm == null)
        {
            return;
        }

        await vm.GoToCurrentDirectoryAsync();
    }

    private async void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var vm = GetViewModel();
        if (vm == null)
        {
            return;
        }

        await vm.StartSearchAsync();
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

        await vm.UploadFilesAsync(files.Select(file => file.Path.LocalPath), GetContextItem(sender) ?? ToDirectoryItem(GetTreeContextNode(sender)));
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

        await vm.UploadFolderAsync(folders[0].Path.LocalPath, GetContextItem(sender) ?? ToDirectoryItem(GetTreeContextNode(sender)));
    }

    private async void DownloadSelectionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await DownloadTargetAsync(sender);
    }

    private async void DownloadItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await DownloadTargetAsync(sender);
    }

    private async void DownloadTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
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

        var target = GetContextItem(sender) ?? ToDirectoryItem(GetTreeContextNode(sender)) ?? vm.SelectedServerItem;
        await vm.DownloadItemAsync(target, folders[0].Path.LocalPath);
    }

    private void CreateDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null)
        {
            return;
        }

        var item = GetContextItem(sender);
        if (item != null)
        {
            vm.ShowCreateDirectoryDialogFor(item);
            return;
        }

        var treeNode = GetTreeContextNode(sender);
        if (treeNode != null)
        {
            vm.ShowCreateDirectoryDialogForTreeNode(treeNode);
            return;
        }

        vm.ShowCreateDirectoryDialogFor(null);
    }

    private void CreateDirectoryForItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowCreateDirectoryDialogFor(GetContextItem(sender));
    }

    private void CreateDirectoryForTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowCreateDirectoryDialogForTreeNode(GetTreeContextNode(sender));
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null)
        {
            return;
        }

        var item = GetContextItem(sender);
        if (item != null)
        {
            vm.ShowDeleteDialogFor(item);
            return;
        }

        var treeNode = GetTreeContextNode(sender);
        if (treeNode != null)
        {
            vm.ShowDeleteDialogForTreeNode(treeNode);
            return;
        }

        vm.ShowDeleteDialogFor(vm.SelectedServerItem);
    }

    private void DeleteItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowDeleteDialogFor(GetContextItem(sender));
    }

    private void DeleteTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowDeleteDialogForTreeNode(GetTreeContextNode(sender));
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

    private async void OpenTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        var node = GetTreeContextNode(sender);
        if (vm == null || node == null)
        {
            return;
        }

        await vm.SelectTreeNodeAsync(node);
    }

    private void UploadFilesForItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        UploadFilesButton_OnClick(sender, e);
    }

    private void UploadFolderForItemMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        UploadFolderButton_OnClick(sender, e);
    }

    private void UploadFilesForTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        UploadFilesButton_OnClick(sender, e);
    }

    private void UploadFolderForTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        UploadFolderButton_OnClick(sender, e);
    }

    private RemoteFileManagerViewModel? GetViewModel() => DataContext as RemoteFileManagerViewModel;

    private static ServerDirectoryItem? GetContextItem(object? sender) =>
        (sender as MenuItem)?.DataContext as ServerDirectoryItem;

    private static RemoteTreeNode? GetTreeContextNode(object? sender) =>
        (sender as MenuItem)?.DataContext as RemoteTreeNode;

    private static ServerDirectoryItem? ToDirectoryItem(RemoteTreeNode? node)
    {
        if (node == null)
        {
            return null;
        }

        return new ServerDirectoryItem
        {
            Name = node.Name,
            FullPath = node.FullPath,
            IsDirectory = node.IsDirectory,
            IsDrive = node.IsDrive,
            Size = 0,
            LastModifiedTime = DateTime.MinValue
        };
    }
}
