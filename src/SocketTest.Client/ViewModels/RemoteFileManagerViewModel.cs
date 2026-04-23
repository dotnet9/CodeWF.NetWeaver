using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using CodeWF.NetWrapper.Requests;
using CodeWF.NetWrapper.Response;
using CodeWF.Tools.Extensions;
using ReactiveUI;
using SocketTest.Client.Models;
using System;
using System.Collections.ObjectModel;
using System.Reactive;

namespace SocketTest.Client.ViewModels;

public class RemoteFileManagerViewModel : ReactiveObject
{
    public RemoteFileManagerViewModel()
    {
        ServerDirectoryItems = new();
        RegisterCommand();
        EventBus.Default.Subscribe(this);
        Logger.Info("远程文件管理初始化完成");
    }

    public ObservableCollection<ServerDirectoryItem> ServerDirectoryItems { get; }

    private ServerDirectoryItem? _selectedServerItem;
    public ServerDirectoryItem? SelectedServerItem
    {
        get => _selectedServerItem;
        set => this.RaiseAndSetIfChanged(ref _selectedServerItem, value);
    }

    private string _currentServerDirectory = @"/";
    public string CurrentServerDirectory
    {
        get => _currentServerDirectory;
        set => this.RaiseAndSetIfChanged(ref _currentServerDirectory, value);
    }

    public TcpSocketClient TcpHelper { get; set; } = new();

    public ReactiveCommand<Unit, Unit> RefreshServerDirectoryCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> EnterParentDirectoryCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> CreateServerDirectoryCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> DeleteServerItemCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> EnterDirectoryCommand { get; private set; } = null!;

    private void RegisterCommand()
    {
        RefreshServerDirectoryCommand = ReactiveCommand.Create(HandleRefreshServerDirectory);
        EnterParentDirectoryCommand = ReactiveCommand.Create(HandleEnterParentDirectory);
        CreateServerDirectoryCommand = ReactiveCommand.Create(HandleCreateServerDirectory);
        DeleteServerItemCommand = ReactiveCommand.Create(HandleDeleteServerItem);
        EnterDirectoryCommand = ReactiveCommand.Create<string>(HandleEnterDirectory);
    }

    private void HandleRefreshServerDirectory()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Warn("未连接服务端，无法刷新目录");
            return;
        }

        ServerDirectoryItems.Clear();
        _ = TcpHelper.SendCommandAsync(new QueryFileStart
        {
            DirectoryPath = CurrentServerDirectory
        });
        Logger.Info($"请求刷新目录：{CurrentServerDirectory}");
    }

    private void HandleEnterParentDirectory()
    {
        if (string.IsNullOrEmpty(CurrentServerDirectory) || CurrentServerDirectory == "/")
        {
            return;
        }

        var parentPath = CurrentServerDirectory.TrimEnd('/');
        var lastSlashIndex = parentPath.LastIndexOf('/');
        CurrentServerDirectory = lastSlashIndex > 0 ? parentPath.Substring(0, lastSlashIndex) + "/" : "/";
        HandleRefreshServerDirectory();
    }

    private void HandleCreateServerDirectory()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Warn("未连接服务端，无法创建目录");
            return;
        }

        var newDirName = "新建目录";
        var newDirPath = CurrentServerDirectory.TrimEnd('/') + "/" + newDirName;
        _ = TcpHelper.SendCommandAsync(new CreateDirectoryStart
        {
            DirectoryPath = newDirPath
        });
        Logger.Info($"请求创建目录：{newDirPath}");
        HandleRefreshServerDirectory();
    }

    private void HandleDeleteServerItem()
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Warn("未连接服务端，无法删除");
            return;
        }

        if (SelectedServerItem == null)
        {
            Logger.Warn("请先选择要删除的项");
            return;
        }

        _ = TcpHelper.SendCommandAsync(new DeleteFileStart
        {
            FilePath = SelectedServerItem.FullPath,
            IsDirectory = SelectedServerItem.IsDirectory
        });
        Logger.Info($"请求删除：{SelectedServerItem.FullPath}");
        HandleRefreshServerDirectory();
    }

    private void HandleEnterDirectory(string directoryName)
    {
        if (!TcpHelper.IsRunning)
        {
            Logger.Warn("未连接服务端，无法进入目录");
            return;
        }

        CurrentServerDirectory = CurrentServerDirectory.TrimEnd('/') + "/" + directoryName;
        HandleRefreshServerDirectory();
    }

    public void ReceivedSocketCommand(SocketCommand message)
    {
        if (message.IsCommand<DirectoryEntryResponse>())
        {
            ReceivedSocketMessage(message.GetCommand<DirectoryEntryResponse>());
        }
        else if (message.IsCommand<DiskInfoListResponse>())
        {
            ReceivedSocketMessage(message.GetCommand<DiskInfoListResponse>());
        }
        else if (message.IsCommand<CreateDirectoryStartAck>())
        {
            ReceivedSocketMessage(message.GetCommand<CreateDirectoryStartAck>());
        }
        else if (message.IsCommand<DeleteFileStartAck>())
        {
            ReceivedSocketMessage(message.GetCommand<DeleteFileStartAck>());
        }
    }

    private void ReceivedSocketMessage(DirectoryEntryResponse response)
    {
        if (response.Entries == null || response.Entries.Count == 0)
        {
            return;
        }

        foreach (var entry in response.Entries)
        {
            var item = new ServerDirectoryItem
            {
                Name = entry.Name,
                FullPath = CurrentServerDirectory.TrimEnd('/') + "/" + entry.Name,
                IsDirectory = entry.EntryType == FileType.Directory,
                Size = entry.Size,
                LastModifiedTime = ((uint)entry.LastModifiedTime).FromSpecialUnixTimeSecondsToDateTime(2000)
            };
            ServerDirectoryItems.Add(item);
        }
    }

    private void ReceivedSocketMessage(DiskInfoListResponse response)
    {
        if (response.Disks == null || response.Disks.Count == 0)
        {
            return;
        }

        foreach (var disk in response.Disks)
        {
            var item = new ServerDirectoryItem
            {
                Name = disk.Name,
                FullPath = disk.Name,
                IsDirectory = true,
                Size = disk.TotalSize,
                LastModifiedTime = DateTime.Now
            };
            ServerDirectoryItems.Add(item);
        }
    }

    private void ReceivedSocketMessage(CreateDirectoryStartAck response)
    {
        if (response.Success)
        {
            Logger.Info($"创建目录成功：{response.DirectoryPath}");
        }
        else
        {
            Logger.Info($"创建目录失败：{response.Message}");
        }
    }

    private void ReceivedSocketMessage(DeleteFileStartAck response)
    {
        if (response.Success)
        {
            Logger.Info($"删除成功：{response.FilePath}");
        }
        else
        {
            Logger.Info($"删除失败：{response.Message}");
        }
    }
}