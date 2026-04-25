using Avalonia.Threading;
using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using CodeWF.NetWrapper.Requests;
using CodeWF.NetWrapper.Response;
using ReactiveUI;
using SocketTest.Client.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace SocketTest.Client.ViewModels;

public class RemoteFileManagerViewModel : ReactiveObject
{
    private readonly TcpSocketClient _tcpHelper;
    private readonly FileTransferViewModel _fileTransferViewModel;
    private readonly ConcurrentDictionary<int, PendingBrowseRequest> _pendingBrowseRequests = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<CreateDirectoryResponse>> _pendingCreateRequests = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<DeletePathResponse>> _pendingDeleteRequests = new();
    private readonly List<ServerDirectoryItem> _currentDirectoryItems = [];
    private readonly List<ServerDirectoryItem> _searchResults = [];
    private string _currentServerDirectory = "/";
    private string _searchKeyword = string.Empty;
    private bool _searchRecursive = true;
    private bool _isListView = true;
    private bool _isBusy;
    private bool _isSearchMode;
    private string _statusText = "等待连接到服务端";
    private int _currentSearchPage = 1;
    private int _searchPageSize = 40;
    private bool _isCreateDialogOpen;
    private string _pendingDirectoryName = "新建文件夹";
    private string _createDialogHint = string.Empty;
    private string _createDirectoryBasePath = "/";
    private bool _isDeleteDialogOpen;
    private string _deleteDialogMessage = string.Empty;
    private ServerDirectoryItem? _selectedServerItem;
    private ServerDirectoryItem? _pendingDeleteItem;
    private CancellationTokenSource? _searchCancellationTokenSource;

    public RemoteFileManagerViewModel(TcpSocketClient tcpHelper, FileTransferViewModel fileTransferViewModel)
    {
        _tcpHelper = tcpHelper;
        _fileTransferViewModel = fileTransferViewModel;

        VisibleItems = new ObservableCollection<ServerDirectoryItem>();

        RefreshServerDirectoryCommand = ReactiveCommand.CreateFromTask(RefreshCurrentDirectoryAsync);
        GoToDirectoryCommand = ReactiveCommand.CreateFromTask(GoToCurrentDirectoryAsync);
        EnterParentDirectoryCommand = ReactiveCommand.CreateFromTask(EnterParentDirectoryAsync);
        OpenItemCommand = ReactiveCommand.CreateFromTask<ServerDirectoryItem>(OpenItemAsync);
        StartSearchCommand = ReactiveCommand.CreateFromTask(StartSearchAsync);
        ClearSearchCommand = ReactiveCommand.Create(ClearSearch);
        PreviousSearchPageCommand = ReactiveCommand.Create(GoToPreviousSearchPage);
        NextSearchPageCommand = ReactiveCommand.Create(GoToNextSearchPage);
        SwitchToListViewCommand = ReactiveCommand.Create(() => { IsListView = true; });
        SwitchToTileViewCommand = ReactiveCommand.Create(() => { IsListView = false; });
        ShowCreateDirectoryDialogCommand = ReactiveCommand.Create<ServerDirectoryItem?>(ShowCreateDirectoryDialog);
        ConfirmCreateDirectoryCommand = ReactiveCommand.CreateFromTask(ConfirmCreateDirectoryAsync);
        CancelCreateDirectoryCommand = ReactiveCommand.Create(CancelCreateDirectoryDialog);
        ShowDeleteDialogCommand = ReactiveCommand.Create<ServerDirectoryItem?>(ShowDeleteDialog);
        ConfirmDeleteCommand = ReactiveCommand.CreateFromTask(ConfirmDeleteAsync);
        CancelDeleteCommand = ReactiveCommand.Create(CancelDeleteDialog);

        EventBus.Default.Subscribe(this);
    }

    public ObservableCollection<ServerDirectoryItem> VisibleItems { get; }

    public ServerDirectoryItem? SelectedServerItem
    {
        get => _selectedServerItem;
        set => this.RaiseAndSetIfChanged(ref _selectedServerItem, value);
    }

    public string CurrentServerDirectory
    {
        get => _currentServerDirectory;
        set => this.RaiseAndSetIfChanged(ref _currentServerDirectory, NormalizeDirectoryInput(value));
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set => this.RaiseAndSetIfChanged(ref _searchKeyword, value);
    }

    public bool SearchRecursive
    {
        get => _searchRecursive;
        set => this.RaiseAndSetIfChanged(ref _searchRecursive, value);
    }

    public bool IsListView
    {
        get => _isListView;
        set
        {
            this.RaiseAndSetIfChanged(ref _isListView, value);
            this.RaisePropertyChanged(nameof(IsTileView));
        }
    }

    public bool IsTileView => !IsListView;

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool IsSearchMode
    {
        get => _isSearchMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSearchMode, value);
            this.RaisePropertyChanged(nameof(SearchSummary));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public int CurrentSearchPage
    {
        get => _currentSearchPage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentSearchPage, value);
            this.RaisePropertyChanged(nameof(SearchSummary));
            this.RaisePropertyChanged(nameof(CanGoPreviousSearchPage));
            this.RaisePropertyChanged(nameof(CanGoNextSearchPage));
        }
    }

    public int SearchPageSize
    {
        get => _searchPageSize;
        set
        {
            var newValue = Math.Max(10, value);
            this.RaiseAndSetIfChanged(ref _searchPageSize, newValue);
            RefreshVisibleItems();
        }
    }

    public int SearchTotalPages => Math.Max(1, (int)Math.Ceiling(_searchResults.Count / (double)SearchPageSize));

    public bool CanGoPreviousSearchPage => CurrentSearchPage > 1;

    public bool CanGoNextSearchPage => CurrentSearchPage < SearchTotalPages;

    public string SearchSummary => IsSearchMode
        ? $"搜索结果 {_searchResults.Count} 项，第 {CurrentSearchPage}/{SearchTotalPages} 页"
        : $"{_currentDirectoryItems.Count} 个项目";

    public bool IsCreateDialogOpen
    {
        get => _isCreateDialogOpen;
        private set => this.RaiseAndSetIfChanged(ref _isCreateDialogOpen, value);
    }

    public string PendingDirectoryName
    {
        get => _pendingDirectoryName;
        set => this.RaiseAndSetIfChanged(ref _pendingDirectoryName, value);
    }

    public string CreateDialogHint
    {
        get => _createDialogHint;
        private set => this.RaiseAndSetIfChanged(ref _createDialogHint, value);
    }

    public bool IsDeleteDialogOpen
    {
        get => _isDeleteDialogOpen;
        private set => this.RaiseAndSetIfChanged(ref _isDeleteDialogOpen, value);
    }

    public string DeleteDialogMessage
    {
        get => _deleteDialogMessage;
        private set => this.RaiseAndSetIfChanged(ref _deleteDialogMessage, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshServerDirectoryCommand { get; }

    public ReactiveCommand<Unit, Unit> GoToDirectoryCommand { get; }

    public ReactiveCommand<Unit, Unit> EnterParentDirectoryCommand { get; }

    public ReactiveCommand<ServerDirectoryItem, Unit> OpenItemCommand { get; }

    public ReactiveCommand<Unit, Unit> StartSearchCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }

    public ReactiveCommand<Unit, Unit> PreviousSearchPageCommand { get; }

    public ReactiveCommand<Unit, Unit> NextSearchPageCommand { get; }

    public ReactiveCommand<Unit, Unit> SwitchToListViewCommand { get; }

    public ReactiveCommand<Unit, Unit> SwitchToTileViewCommand { get; }

    public ReactiveCommand<ServerDirectoryItem?, Unit> ShowCreateDirectoryDialogCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmCreateDirectoryCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCreateDirectoryCommand { get; }

    public ReactiveCommand<ServerDirectoryItem?, Unit> ShowDeleteDialogCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmDeleteCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelDeleteCommand { get; }

    public async Task UploadFilesAsync(IEnumerable<string> localFilePaths, ServerDirectoryItem? targetDirectory = null)
    {
        if (!EnsureConnected())
        {
            return;
        }

        var files = localFilePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            StatusText = "没有可上传的文件。";
            return;
        }

        var remoteDirectory = GetTargetDirectoryPath(targetDirectory);
        var transfers = files.Select(file => (file, CombineRemotePath(remoteDirectory, Path.GetFileName(file)))).ToList();
        _fileTransferViewModel.EnqueueUploads(transfers);
        StatusText = $"已添加 {transfers.Count} 个上传任务。";
    }

    public void ShowCreateDirectoryDialogFor(ServerDirectoryItem? item) => ShowCreateDirectoryDialog(item);

    public void ShowDeleteDialogFor(ServerDirectoryItem? item) => ShowDeleteDialog(item);

    public Task OpenItemFromMenuAsync(ServerDirectoryItem item) => OpenItemAsync(item);

    public async Task UploadFolderAsync(string localFolderPath, ServerDirectoryItem? targetDirectory = null)
    {
        if (!EnsureConnected())
        {
            return;
        }

        if (!Directory.Exists(localFolderPath))
        {
            StatusText = "所选文件夹不存在。";
            return;
        }

        var folderName = Path.GetFileName(localFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var remoteDirectory = GetTargetDirectoryPath(targetDirectory);
        var remoteRootPath = CombineRemotePath(remoteDirectory, folderName);

        await CreateDirectoryRequestAsync(remoteRootPath);
        foreach (var subDirectory in Directory.GetDirectories(localFolderPath, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(localFolderPath, subDirectory).Replace('\\', '/');
            await CreateDirectoryRequestAsync(CombineRemotePath(remoteRootPath, relativeDirectory));
        }

        var uploads = Directory.GetFiles(localFolderPath, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                var relativePath = Path.GetRelativePath(localFolderPath, file).Replace('\\', '/');
                return (LocalPath: file, RemotePath: CombineRemotePath(remoteRootPath, relativePath));
            })
            .ToList();

        _fileTransferViewModel.EnqueueUploads(uploads);
        StatusText = $"已添加文件夹上传任务，共 {uploads.Count} 个文件。";
    }

    public async Task DownloadItemAsync(ServerDirectoryItem? item, string localRootDirectory)
    {
        if (!EnsureConnected())
        {
            return;
        }

        if (item == null)
        {
            StatusText = "请先选择要下载的文件或文件夹。";
            return;
        }

        Directory.CreateDirectory(localRootDirectory);

        if (!item.IsDirectory)
        {
            _fileTransferViewModel.EnqueueDownloads((item.FullPath, Path.Combine(localRootDirectory, item.Name)));
            StatusText = $"已添加文件下载任务：{item.Name}";
            return;
        }

        var downloads = new List<(string RemotePath, string LocalPath)>();
        var baseLocalDirectory = Path.Combine(localRootDirectory, item.Name);
        Directory.CreateDirectory(baseLocalDirectory);

        var queue = new Queue<(string RemotePath, string RelativePath)>();
        queue.Enqueue((item.FullPath, string.Empty));

        while (queue.Count > 0)
        {
            var (remotePath, relativePath) = queue.Dequeue();
            var browseResult = await BrowseDirectoryAsync(remotePath);
            foreach (var child in browseResult)
            {
                if (child.IsDirectory)
                {
                    var nextRelativePath = Path.Combine(relativePath, child.Name);
                    Directory.CreateDirectory(Path.Combine(baseLocalDirectory, nextRelativePath));
                    queue.Enqueue((child.FullPath, nextRelativePath));
                }
                else
                {
                    var localPath = Path.Combine(baseLocalDirectory, relativePath, child.Name);
                    downloads.Add((child.FullPath, localPath));
                }
            }
        }

        _fileTransferViewModel.EnqueueDownloads(downloads);
        StatusText = $"已添加文件夹下载任务，共 {downloads.Count} 个文件。";
    }

    private async Task RefreshCurrentDirectoryAsync()
    {
        if (!EnsureConnected())
        {
            return;
        }

        IsBusy = true;
        try
        {
            ClearSearchInternal();
            var items = await BrowseDirectoryAsync(CurrentServerDirectory);
            _currentDirectoryItems.Clear();
            _currentDirectoryItems.AddRange(items.OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name));
            RefreshVisibleItems();
            StatusText = $"已打开目录：{CurrentServerDirectory}";
        }
        catch (Exception ex)
        {
            Logger.Error($"浏览目录失败：{CurrentServerDirectory}", ex);
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task GoToCurrentDirectoryAsync() => RefreshCurrentDirectoryAsync();

    private async Task EnterParentDirectoryAsync()
    {
        var parent = GetParentRemotePath(CurrentServerDirectory);
        if (string.Equals(parent, CurrentServerDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentServerDirectory = parent;
        await RefreshCurrentDirectoryAsync();
    }

    private async Task OpenItemAsync(ServerDirectoryItem item)
    {
        if (item.IsDirectory)
        {
            CurrentServerDirectory = item.FullPath;
            await RefreshCurrentDirectoryAsync();
            return;
        }

        if (IsSearchMode)
        {
            CurrentServerDirectory = GetParentRemotePath(item.FullPath);
            await RefreshCurrentDirectoryAsync();
            SelectedServerItem = VisibleItems.FirstOrDefault(entry =>
                string.Equals(NormalizePath(entry.FullPath), NormalizePath(item.FullPath), StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task StartSearchAsync()
    {
        if (!EnsureConnected())
        {
            return;
        }

        var keyword = SearchKeyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ClearSearch();
            return;
        }

        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource?.Dispose();
        _searchCancellationTokenSource = new CancellationTokenSource();

        IsBusy = true;
        IsSearchMode = true;
        _searchResults.Clear();
        CurrentSearchPage = 1;
        RefreshVisibleItems();

        try
        {
            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            queue.Enqueue(CurrentServerDirectory);

            while (queue.Count > 0)
            {
                _searchCancellationTokenSource.Token.ThrowIfCancellationRequested();

                var remotePath = queue.Dequeue();
                if (!visited.Add(NormalizePath(remotePath)))
                {
                    continue;
                }

                StatusText = $"正在搜索：已扫描 {visited.Count} 个目录";
                var items = await BrowseDirectoryAsync(remotePath, _searchCancellationTokenSource.Token);
                foreach (var item in items)
                {
                    if (item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        _searchResults.Add(item);
                    }

                    if (SearchRecursive && item.IsDirectory)
                    {
                        queue.Enqueue(item.FullPath);
                    }
                }

                RefreshVisibleItems();
            }

            StatusText = $"搜索完成，共找到 {_searchResults.Count} 个匹配项。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "搜索已取消。";
        }
        catch (Exception ex)
        {
            Logger.Error("远程搜索失败", ex);
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshVisibleItems();
        }
    }

    private void ClearSearch()
    {
        _searchCancellationTokenSource?.Cancel();
        ClearSearchInternal();
        RefreshVisibleItems();
        StatusText = $"已返回目录浏览：{CurrentServerDirectory}";
    }

    private void ClearSearchInternal()
    {
        IsSearchMode = false;
        _searchResults.Clear();
        CurrentSearchPage = 1;
    }

    private void GoToPreviousSearchPage()
    {
        if (!CanGoPreviousSearchPage)
        {
            return;
        }

        CurrentSearchPage--;
        RefreshVisibleItems();
    }

    private void GoToNextSearchPage()
    {
        if (!CanGoNextSearchPage)
        {
            return;
        }

        CurrentSearchPage++;
        RefreshVisibleItems();
    }

    private void ShowCreateDirectoryDialog(ServerDirectoryItem? targetItem)
    {
        _createDirectoryBasePath = targetItem?.IsDirectory == true
            ? targetItem.FullPath
            : CurrentServerDirectory;
        CreateDialogHint = $"将在 {_createDirectoryBasePath} 中创建目录";
        PendingDirectoryName = "新建文件夹";
        IsCreateDialogOpen = true;
    }

    private async Task ConfirmCreateDirectoryAsync()
    {
        var directoryName = PendingDirectoryName.Trim();
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            StatusText = "目录名称不能为空。";
            return;
        }

        IsCreateDialogOpen = false;
        try
        {
            var remotePath = CombineRemotePath(_createDirectoryBasePath, directoryName);
            await CreateDirectoryRequestAsync(remotePath);
            StatusText = $"已创建目录：{remotePath}";
            await RefreshCurrentDirectoryAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("创建远程目录失败", ex);
            StatusText = ex.Message;
        }
    }

    private void CancelCreateDirectoryDialog()
    {
        IsCreateDialogOpen = false;
    }

    private void ShowDeleteDialog(ServerDirectoryItem? item)
    {
        _pendingDeleteItem = item ?? SelectedServerItem;
        if (_pendingDeleteItem == null)
        {
            StatusText = "请先选择要删除的文件或文件夹。";
            return;
        }

        DeleteDialogMessage = _pendingDeleteItem.IsDirectory
            ? $"确定删除文件夹“{_pendingDeleteItem.Name}”吗？空文件夹会立即删除。"
            : $"确定删除文件“{_pendingDeleteItem.Name}”吗？";
        IsDeleteDialogOpen = true;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteItem == null)
        {
            IsDeleteDialogOpen = false;
            return;
        }

        var item = _pendingDeleteItem;
        IsDeleteDialogOpen = false;

        try
        {
            await DeletePathRequestAsync(item.FullPath, item.IsDirectory);
            StatusText = $"已删除：{item.FullPath}";
            await RefreshCurrentDirectoryAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("删除远程文件失败", ex);
            StatusText = ex.Message;
        }
        finally
        {
            _pendingDeleteItem = null;
        }
    }

    private void CancelDeleteDialog()
    {
        _pendingDeleteItem = null;
        IsDeleteDialogOpen = false;
    }

    private async Task<List<ServerDirectoryItem>> BrowseDirectoryAsync(string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedRequestPath = string.Equals(directoryPath, "/", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : directoryPath;
        var taskId = NetHelper.GetTaskId();
        var tcs = new TaskCompletionSource<List<ServerDirectoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingBrowseRequest(directoryPath, tcs);
        _pendingBrowseRequests[taskId] = request;

        using var registration = cancellationToken.Register(() =>
        {
            if (_pendingBrowseRequests.TryRemove(taskId, out var pending))
            {
                pending.CompletionSource.TrySetCanceled(cancellationToken);
            }
        });

        await _tcpHelper.SendCommandAsync(new BrowseFileSystemRequest
        {
            TaskId = taskId,
            DirectoryPath = normalizedRequestPath
        });

        return await tcs.Task;
    }

    private async Task CreateDirectoryRequestAsync(string directoryPath)
    {
        var taskId = NetHelper.GetTaskId();
        var tcs = new TaskCompletionSource<CreateDirectoryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCreateRequests[taskId] = tcs;

        await _tcpHelper.SendCommandAsync(new CreateDirectoryRequest
        {
            TaskId = taskId,
            DirectoryPath = directoryPath
        });

        var response = await tcs.Task;
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }
    }

    private async Task DeletePathRequestAsync(string filePath, bool isDirectory)
    {
        var taskId = NetHelper.GetTaskId();
        var tcs = new TaskCompletionSource<DeletePathResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingDeleteRequests[taskId] = tcs;

        await _tcpHelper.SendCommandAsync(new DeletePathRequest
        {
            TaskId = taskId,
            FilePath = filePath,
            IsDirectory = isDirectory
        });

        var response = await tcs.Task;
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }
    }

    private void RefreshVisibleItems()
    {
        VisibleItems.Clear();
        var source = IsSearchMode
            ? _searchResults.Skip((CurrentSearchPage - 1) * SearchPageSize).Take(SearchPageSize)
            : _currentDirectoryItems;

        foreach (var item in source)
        {
            VisibleItems.Add(item);
        }

        this.RaisePropertyChanged(nameof(SearchSummary));
        this.RaisePropertyChanged(nameof(CanGoPreviousSearchPage));
        this.RaisePropertyChanged(nameof(CanGoNextSearchPage));
    }

    private bool EnsureConnected()
    {
        if (_tcpHelper.IsRunning)
        {
            return true;
        }

        StatusText = "请先在“进程监控”模块连接 TCP 服务端。";
        return false;
    }

    private string GetTargetDirectoryPath(ServerDirectoryItem? targetDirectory)
    {
        if (targetDirectory?.IsDirectory == true)
        {
            return targetDirectory.FullPath;
        }

        return CurrentServerDirectory;
    }

    private static string NormalizeDirectoryInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/";
        }

        return value.Trim();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static string GetParentRemotePath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || currentPath == "/")
        {
            return "/";
        }

        var normalized = currentPath.TrimEnd('/', '\\');
        if (normalized.EndsWith(":", StringComparison.Ordinal))
        {
            return "/";
        }

        var lastSlash = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        if (lastSlash <= 0)
        {
            return "/";
        }

        if (lastSlash == 2 && normalized.Length >= 2 && normalized[1] == ':')
        {
            return normalized.Substring(0, 3);
        }

        return normalized.Substring(0, lastSlash);
    }

    private static string CombineRemotePath(string basePath, string childName)
    {
        if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            return childName.Replace('\\', '/');
        }

        if (basePath.Contains(':') || basePath.Contains('\\'))
        {
            return Path.Combine(basePath, childName.Replace('/', Path.DirectorySeparatorChar));
        }

        return $"{basePath.TrimEnd('/')}/{childName.TrimStart('/')}";
    }

    public void ReceivedSocketCommand(SocketCommand message)
    {
        if (message.IsCommand<BrowseFileSystemResponse>())
        {
            HandleBrowseResponse(message.GetCommand<BrowseFileSystemResponse>());
        }
        else if (message.IsCommand<DriveListResponse>())
        {
            HandleDriveListResponse(message.GetCommand<DriveListResponse>());
        }
        else if (message.IsCommand<CreateDirectoryResponse>())
        {
            HandleCreateDirectoryResponse(message.GetCommand<CreateDirectoryResponse>());
        }
        else if (message.IsCommand<DeletePathResponse>())
        {
            HandleDeletePathResponse(message.GetCommand<DeletePathResponse>());
        }
        else if (message.IsCommand<FileTransferReject>())
        {
            HandleRemoteReject(message.GetCommand<FileTransferReject>());
        }
    }

    private void HandleBrowseResponse(BrowseFileSystemResponse response)
    {
        if (!_pendingBrowseRequests.TryGetValue(response.TaskId, out var pendingRequest))
        {
            return;
        }

        lock (pendingRequest.SyncRoot)
        {
            pendingRequest.ExpectedPages = Math.Max(response.PageCount, 1);
            pendingRequest.ReceivedPages++;

            if (response.Entries != null)
            {
                foreach (var entry in response.Entries)
                {
                    pendingRequest.Items.Add(ToServerDirectoryItem(pendingRequest.RequestedPath, entry));
                }
            }

            if (pendingRequest.ReceivedPages < pendingRequest.ExpectedPages)
            {
                return;
            }
        }

        if (_pendingBrowseRequests.TryRemove(response.TaskId, out var completedRequest))
        {
            completedRequest.CompletionSource.TrySetResult(completedRequest.Items);
        }
    }

    private void HandleDriveListResponse(DriveListResponse response)
    {
        if (!_pendingBrowseRequests.TryRemove(response.TaskId, out var pendingRequest))
        {
            return;
        }

        var items = response.Disks?
            .Select(disk => new ServerDirectoryItem
            {
                Name = disk.Name,
                FullPath = disk.Name,
                IsDirectory = true,
                Size = disk.TotalSize,
                LastModifiedTime = DateTime.MinValue
            })
            .OrderBy(item => item.Name)
            .ToList() ?? [];

        pendingRequest.CompletionSource.TrySetResult(items);
    }

    private void HandleCreateDirectoryResponse(CreateDirectoryResponse response)
    {
        if (_pendingCreateRequests.TryRemove(response.TaskId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }

    private void HandleDeletePathResponse(DeletePathResponse response)
    {
        if (_pendingDeleteRequests.TryRemove(response.TaskId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }

    private void HandleRemoteReject(FileTransferReject reject)
    {
        if (_pendingBrowseRequests.TryRemove(reject.TaskId, out var browseRequest))
        {
            browseRequest.CompletionSource.TrySetException(new InvalidOperationException(reject.Message));
            return;
        }

        if (_pendingCreateRequests.TryRemove(reject.TaskId, out var createRequest))
        {
            createRequest.TrySetException(new InvalidOperationException(reject.Message));
            return;
        }

        if (_pendingDeleteRequests.TryRemove(reject.TaskId, out var deleteRequest))
        {
            deleteRequest.TrySetException(new InvalidOperationException(reject.Message));
        }
    }

    private static ServerDirectoryItem ToServerDirectoryItem(string requestedPath, FileSystemEntry entry)
    {
        var normalizedBasePath = string.IsNullOrWhiteSpace(requestedPath) ? "/" : requestedPath;
        return new ServerDirectoryItem
        {
            Name = entry.Name,
            FullPath = CombineRemotePath(normalizedBasePath, entry.Name),
            IsDirectory = entry.EntryType == FileType.Directory,
            Size = entry.Size,
            LastModifiedTime = entry.LastModifiedTime > 0
                ? new DateTime(entry.LastModifiedTime)
                : DateTime.MinValue
        };
    }

    private sealed class PendingBrowseRequest
    {
        public PendingBrowseRequest(string requestedPath, TaskCompletionSource<List<ServerDirectoryItem>> completionSource)
        {
            RequestedPath = requestedPath;
            CompletionSource = completionSource;
        }

        public string RequestedPath { get; }

        public TaskCompletionSource<List<ServerDirectoryItem>> CompletionSource { get; }

        public List<ServerDirectoryItem> Items { get; } = [];

        public int ExpectedPages { get; set; }

        public int ReceivedPages { get; set; }

        public object SyncRoot { get; } = new();
    }
}
