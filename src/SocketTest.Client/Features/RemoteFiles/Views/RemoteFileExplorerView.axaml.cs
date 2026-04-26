using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SocketTest.Client.Features.RemoteFiles.Models;
using SocketTest.Client.Features.RemoteFiles.ViewModels;
using SocketTest.Client.Shell.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SocketTest.Client.Features.RemoteFiles.Views;

public partial class RemoteFileExplorerView : UserControl
{
    public RemoteFileExplorerView()
    {
        InitializeComponent();
        DataContext = ClientShellViewModelRegistry.RemoteFileExplorerViewModel;
    }

    private async void NavigationTree_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GetViewModel() is not { } viewModel || e.AddedItems.OfType<RemoteDirectoryNode>().FirstOrDefault() is not { } node)
        {
            return;
        }

        await viewModel.SelectNodeAsync(node);
    }

    private async void CurrentDirectoryTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && GetViewModel() is { } viewModel)
        {
            await viewModel.GoToCurrentDirectoryAsync();
        }
    }

    private async void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && GetViewModel() is { } viewModel)
        {
            await viewModel.StartSearchAsync();
        }
    }

    private async void UploadFilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetViewModelAndTopLevel(out var viewModel, out var topLevel))
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

        await viewModel.UploadFilesAsync(
            files.Select(file => file.Path.LocalPath),
            GetContextEntry(sender) ?? ToEntry(GetTreeContextNode(sender)));
    }

    private async void UploadFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetViewModelAndTopLevel(out var viewModel, out var topLevel))
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

        await viewModel.UploadFolderAsync(
            folders[0].Path.LocalPath,
            GetContextEntry(sender) ?? ToEntry(GetTreeContextNode(sender)));
    }

    private async void DownloadSelectionButton_OnClick(object? sender, RoutedEventArgs e) => await DownloadTargetAsync(sender);

    private async void DownloadEntryMenuItem_OnClick(object? sender, RoutedEventArgs e) => await DownloadTargetAsync(sender);

    private async void DownloadTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e) => await DownloadTargetAsync(sender);

    private async Task DownloadTargetAsync(object? sender)
    {
        if (!TryGetViewModelAndTopLevel(out var viewModel, out var topLevel))
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

        var target = GetContextEntry(sender) ?? ToEntry(GetTreeContextNode(sender)) ?? viewModel.SelectedEntry;
        await viewModel.DownloadEntryAsync(target, folders[0].Path.LocalPath);
    }

    private void CreateDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetViewModel() is not { } viewModel)
        {
            return;
        }

        if (GetContextEntry(sender) is { } entry)
        {
            viewModel.ShowCreateDirectoryDialogFor(entry);
            return;
        }

        if (GetTreeContextNode(sender) is { } node)
        {
            viewModel.ShowCreateDirectoryDialogForNode(node);
            return;
        }

        viewModel.ShowCreateDirectoryDialogFor(null);
    }

    private void CreateDirectoryForEntryMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowCreateDirectoryDialogFor(GetContextEntry(sender));
    }

    private void CreateDirectoryForTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowCreateDirectoryDialogForNode(GetTreeContextNode(sender));
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetViewModel() is not { } viewModel)
        {
            return;
        }

        if (GetContextEntry(sender) is { } entry)
        {
            viewModel.ShowDeleteDialogFor(entry);
            return;
        }

        if (GetTreeContextNode(sender) is { } node)
        {
            viewModel.ShowDeleteDialogForNode(node);
            return;
        }

        viewModel.ShowDeleteDialogFor(viewModel.SelectedEntry);
    }

    private void DeleteEntryMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowDeleteDialogFor(GetContextEntry(sender));
    }

    private void DeleteTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        GetViewModel()?.ShowDeleteDialogForNode(GetTreeContextNode(sender));
    }

    private async void OpenEntryMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetViewModel() is { } viewModel && GetContextEntry(sender) is { } entry)
        {
            await viewModel.OpenEntryFromMenuAsync(entry);
        }
    }

    private async void OpenTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetViewModel() is { } viewModel && GetTreeContextNode(sender) is { } node)
        {
            await viewModel.SelectNodeAsync(node);
        }
    }

    private void UploadFilesForEntryMenuItem_OnClick(object? sender, RoutedEventArgs e) => UploadFilesButton_OnClick(sender, e);

    private void UploadFolderForEntryMenuItem_OnClick(object? sender, RoutedEventArgs e) => UploadFolderButton_OnClick(sender, e);

    private void UploadFilesForTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e) => UploadFilesButton_OnClick(sender, e);

    private void UploadFolderForTreeNodeMenuItem_OnClick(object? sender, RoutedEventArgs e) => UploadFolderButton_OnClick(sender, e);

    private RemoteFileExplorerViewModel? GetViewModel() => DataContext as RemoteFileExplorerViewModel;

    private bool TryGetViewModelAndTopLevel(out RemoteFileExplorerViewModel viewModel, out TopLevel topLevel)
    {
        viewModel = GetViewModel()!;
        topLevel = TopLevel.GetTopLevel(this)!;
        return viewModel != null && topLevel != null;
    }

    private static RemoteFileEntry? GetContextEntry(object? sender) => (sender as MenuItem)?.DataContext as RemoteFileEntry;

    private static RemoteDirectoryNode? GetTreeContextNode(object? sender) => (sender as MenuItem)?.DataContext as RemoteDirectoryNode;

    private static RemoteFileEntry? ToEntry(RemoteDirectoryNode? node)
    {
        if (node == null)
        {
            return null;
        }

        return new RemoteFileEntry
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
