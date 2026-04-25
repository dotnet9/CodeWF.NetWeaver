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
    private string _statusText = "请先连接到服务端。";
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
    private RemoteTreeNode? _selectedTreeNode;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private bool _rootDisplaysDriveList = true;

    public RemoteFileManagerViewModel(TcpSocketClient tcpHelper, FileTransferViewModel fileTransferViewModel)
    {
        _tcpHelper = tcpHelper;
        _fileTransferViewModel = fileTransferViewModel;

        NavigationRoots = [];
        VisibleItems = [];

        EventBus.Default.Subscribe(this);
    }

    public ObservableCollection<RemoteTreeNode> NavigationRoots { get; }

    public ObservableCollection<ServerDirectoryItem> VisibleItems { get; }

    public RemoteTreeNode? SelectedTreeNode
    {
        get => _selectedTreeNode;
        private set => this.RaiseAndSetIfChanged(ref _selectedTreeNode, value);
    }

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
            this.RaisePropertyChanged(nameof(IsBrowseMode));
        }
    }

    public bool IsBrowseMode => !IsSearchMode;

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
        ? $"搜索结果 {_searchResults.Count} 项，页码 {CurrentSearchPage}/{SearchTotalPages}"
        : $"当前目录 {_currentDirectoryItems.Count} 项";

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

    public Task RefreshServerDirectoryCommand() => RefreshCurrentDirectoryAsync();

    public Task GoToDirectoryCommand() => GoToCurrentDirectoryAsync();

    public Task EnterParentDirectoryCommand() => EnterParentDirectoryAsync();

    public Task OpenItemCommand(ServerDirectoryItem item) => OpenItemAsync(item);

    public Task StartSearchCommand() => StartSearchAsync();

    public void ClearSearchCommand() => ClearSearch();

    public void PreviousSearchPageCommand() => GoToPreviousSearchPage();

    public void NextSearchPageCommand() => GoToNextSearchPage();

    public void SwitchToListViewCommand() => IsListView = true;

    public void SwitchToTileViewCommand() => IsListView = false;

    public void ShowCreateDirectoryDialogCommand(ServerDirectoryItem? item) => ShowCreateDirectoryDialog(item);

    public Task ConfirmCreateDirectoryCommand() => ConfirmCreateDirectoryAsync();

    public void CancelCreateDirectoryCommand() => CancelCreateDirectoryDialog();

    public void ShowDeleteDialogCommand(ServerDirectoryItem? item) => ShowDeleteDialog(item);

    public Task ConfirmDeleteCommand() => ConfirmDeleteAsync();

    public void CancelDeleteCommand() => CancelDeleteDialog();

    public async Task HandleConnectionStateChangedAsync(bool isConnected)
    {
        if (isConnected)
        {
            await InitializeExplorerAsync();
            return;
        }

        ResetExplorer("连接已断开，请重新连接服务端。");
    }

    public async Task InitializeExplorerAsync()
    {
        if (!EnsureConnected())
        {
            return;
        }

        IsBusy = true;
        try
        {
            ClearSearchInternal();

            var rootNode = GetOrCreateRootNode();
            var rootItems = await BrowseDirectoryAsync("/");

            ApplyDirectoryItems("/", rootItems);
            PopulateNodeChildren(rootNode, rootItems);

            rootNode.IsExpanded = true;
            rootNode.IsSelected = true;
            SelectedTreeNode = rootNode;
            StatusText = "已连接服务端，正在浏览远程文件系统。";
        }
        catch (Exception ex)
        {
            Logger.Error("初始化远程文件管理器失败", ex);
            ResetExplorer(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectTreeNodeAsync(RemoteTreeNode? node)
    {
        if (node == null || node.IsPlaceholder || !EnsureConnected())
        {
            return;
        }

        SelectedTreeNode = node;
        node.IsSelected = true;
        await NavigateToPathAsync(node.FullPath);
    }

    public async Task ExpandTreeNodeAsync(RemoteTreeNode? node)
    {
        if (node == null || node.IsPlaceholder || !node.IsDirectory || !EnsureConnected())
        {
            return;
        }

        if (node.ChildrenLoaded)
        {
            node.IsExpanded = true;
            return;
        }

        node.IsLoading = true;
        try
        {
            var items = PathsEqual(node.FullPath, CurrentServerDirectory) && !IsSearchMode
                ? _currentDirectoryItems.ToList()
                : await BrowseDirectoryAsync(node.FullPath);

            PopulateNodeChildren(node, items);
            node.IsExpanded = true;
        }
        catch (Exception ex)
        {
            Logger.Error($"加载目录树节点失败：{node.FullPath}", ex);
            StatusText = ex.Message;
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    public async Task UploadFilesAsync(IEnumerable<string> localFilePaths, ServerDirectoryItem? targetDirectory = null)
    {
        if (!TryGetWritableTargetDirectory(targetDirectory, out var remoteDirectory))
        {
            return;
        }

        var files = localFilePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            StatusText = "没有可上传的文件。";
            return;
        }

        var transfers = files.Select(file => (file, CombineRemotePath(remoteDirectory, Path.GetFileName(file)))).ToList();
        _fileTransferViewModel.EnqueueUploads(transfers);
        StatusText = $"已加入 {transfers.Count} 个上传任务。";
    }

    public void ShowCreateDirectoryDialogFor(ServerDirectoryItem? item) => ShowCreateDirectoryDialog(item);

    public void ShowCreateDirectoryDialogForTreeNode(RemoteTreeNode? node)
    {
        if (node == null || node.IsPlaceholder)
        {
            return;
        }

        ShowCreateDirectoryDialog(ToDirectoryItem(node));
    }

    public void ShowDeleteDialogFor(ServerDirectoryItem? item) => ShowDeleteDialog(item);

    public void ShowDeleteDialogForTreeNode(RemoteTreeNode? node)
    {
        if (node == null || node.IsPlaceholder)
        {
            return;
        }

        ShowDeleteDialog(ToDirectoryItem(node));
    }

    public Task OpenItemFromMenuAsync(ServerDirectoryItem item) => OpenItemAsync(item);

    public async Task UploadFolderAsync(string localFolderPath, ServerDirectoryItem? targetDirectory = null)
    {
        if (!TryGetWritableTargetDirectory(targetDirectory, out var remoteDirectory))
        {
            return;
        }

        if (!Directory.Exists(localFolderPath))
        {
            StatusText = "所选文件夹不存在。";
            return;
        }

        var folderName = Path.GetFileName(localFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var remoteRootPath = CombineRemotePath(remoteDirectory, folderName);

        try
        {
            await CreateDirectoryRequestAsync(remoteRootPath);
            foreach (var subDirectory in Directory.GetDirectories(localFolderPath, "*", SearchOption.AllDirectories))
            {
                var relativeDirectory = Path.GetRelativePath(localFolderPath, subDirectory).Replace('\\', '/');
                await CreateDirectoryRequestAsync(CombineRemotePath(remoteRootPath, relativeDirectory));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"创建远程目录结构失败：{remoteRootPath}", ex);
            StatusText = ex.Message;
            return;
        }

        var uploads = Directory.GetFiles(localFolderPath, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                var relativePath = Path.GetRelativePath(localFolderPath, file).Replace('\\', '/');
                return (LocalPath: file, RemotePath: CombineRemotePath(remoteRootPath, relativePath));
            })
            .ToList();

        _fileTransferViewModel.EnqueueUploads(uploads);
        StatusText = $"已加入文件夹上传任务，共 {uploads.Count} 个文件。";
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

        if (item.IsDrive)
        {
            StatusText = "请先进入磁盘后再选择具体目录或文件下载。";
            return;
        }

        Directory.CreateDirectory(localRootDirectory);

        if (!item.IsDirectory)
        {
            _fileTransferViewModel.EnqueueDownloads((item.FullPath, Path.Combine(localRootDirectory, item.Name)));
            StatusText = $"已加入下载任务：{item.Name}";
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
        StatusText = $"已加入文件夹下载任务，共 {downloads.Count} 个文件。";
    }

    public Task RefreshCurrentDirectoryAsync() => NavigateToPathAsync(CurrentServerDirectory);

    public Task GoToCurrentDirectoryAsync() => NavigateToPathAsync(CurrentServerDirectory);

    public async Task EnterParentDirectoryAsync()
    {
        var parent = GetParentRemotePath(CurrentServerDirectory);
        if (PathsEqual(parent, CurrentServerDirectory))
        {
            return;
        }

        await NavigateToPathAsync(parent);
    }

    public async Task NavigateToPathAsync(string directoryPath, string? selectItemFullPath = null)
    {
        if (!EnsureConnected())
        {
            return;
        }

        IsBusy = true;
        try
        {
            var normalizedPath = NormalizeDirectoryInput(directoryPath);
            ClearSearchInternal();

            var items = await BrowseDirectoryAsync(normalizedPath);
            ApplyDirectoryItems(normalizedPath, items);

            var matchedNode = await EnsureTreeNodeForPathAsync(normalizedPath, items);
            if (matchedNode != null)
            {
                matchedNode.IsExpanded = true;
                matchedNode.IsSelected = true;
                SelectedTreeNode = matchedNode;
            }

            if (!string.IsNullOrWhiteSpace(selectItemFullPath))
            {
                SelectVisibleItem(selectItemFullPath);
            }

            StatusText = $"已打开目录：{CurrentServerDirectory}";
        }
        catch (Exception ex)
        {
            Logger.Error($"打开目录失败：{directoryPath}", ex);
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenItemAsync(ServerDirectoryItem item)
    {
        if (item.IsDirectory)
        {
            await NavigateToPathAsync(item.FullPath);
            return;
        }

        if (IsSearchMode)
        {
            await NavigateToPathAsync(GetParentRemotePath(item.FullPath), item.FullPath);
            return;
        }

        SelectedServerItem = item;
        StatusText = $"已选中文件：{item.Name}。可通过右键菜单或工具栏执行下载。";
    }

    public async Task StartSearchAsync()
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
                if (!visited.Add(NormalizeComparisonPath(remotePath)))
                {
                    continue;
                }

                StatusText = $"正在搜索，已扫描 {visited.Count} 个目录...";
                var items = await BrowseDirectoryAsync(remotePath, _searchCancellationTokenSource.Token);
                foreach (var item in items)
                {
                    if (item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        _searchResults.Add(item);
                    }

                    if (SearchRecursive && item.IsDirectory && !item.IsDrive)
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
        var targetPath = targetItem?.IsDirectory == true
            ? targetItem.FullPath
            : CurrentServerDirectory;

        if (!CanWriteToPath(targetPath))
        {
            StatusText = "请先进入具体磁盘或目录后再创建文件夹。";
            return;
        }

        _createDirectoryBasePath = targetPath;
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
            await NavigateToPathAsync(_createDirectoryBasePath);
            StatusText = $"已创建目录：{remotePath}";
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

        if (_pendingDeleteItem.IsDrive)
        {
            StatusText = "不支持删除磁盘根节点。";
            _pendingDeleteItem = null;
            return;
        }

        DeleteDialogMessage = _pendingDeleteItem.IsDirectory
            ? $"确认删除文件夹“{_pendingDeleteItem.Name}”吗？目录必须为空才能删除。"
            : $"确认删除文件“{_pendingDeleteItem.Name}”吗？";
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
        _pendingDeleteItem = null;
        IsDeleteDialogOpen = false;

        try
        {
            await DeletePathRequestAsync(item.FullPath, item.IsDirectory);

            var refreshPath = item.IsDirectory && PathsEqual(item.FullPath, CurrentServerDirectory)
                ? GetParentRemotePath(item.FullPath)
                : CurrentServerDirectory;

            await NavigateToPathAsync(refreshPath);
            StatusText = $"已删除：{item.FullPath}";
        }
        catch (Exception ex)
        {
            Logger.Error("删除远程路径失败", ex);
            StatusText = ex.Message;
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
        var normalizedRequestPath = PathsEqual(directoryPath, "/")
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

    private async Task<RemoteTreeNode?> EnsureTreeNodeForPathAsync(string directoryPath,
        IReadOnlyCollection<ServerDirectoryItem>? currentItems = null)
    {
        var normalizedPath = NormalizeDirectoryInput(directoryPath);
        var rootNode = await EnsureRootNodeLoadedAsync(PathsEqual(normalizedPath, "/") ? currentItems : null);
        if (rootNode == null)
        {
            return null;
        }

        if (PathsEqual(normalizedPath, "/"))
        {
            return rootNode;
        }

        var currentNode = FindChildByPath(rootNode, GetDriveRootPath(normalizedPath) ?? normalizedPath);
        if (currentNode == null)
        {
            return rootNode;
        }

        foreach (var segment in GetRelativeSegments(normalizedPath))
        {
            if (!currentNode.ChildrenLoaded)
            {
                await ExpandTreeNodeAsync(currentNode);
            }

            var nextNode = currentNode.Children.FirstOrDefault(child =>
                !child.IsPlaceholder &&
                string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (nextNode == null)
            {
                return currentNode;
            }

            currentNode.IsExpanded = true;
            currentNode = nextNode;
        }

        if (currentItems != null)
        {
            PopulateNodeChildren(currentNode, currentItems);
        }

        return currentNode;
    }

    private async Task<RemoteTreeNode?> EnsureRootNodeLoadedAsync(IReadOnlyCollection<ServerDirectoryItem>? rootItems = null)
    {
        var rootNode = GetOrCreateRootNode();
        if (rootNode.ChildrenLoaded)
        {
            return rootNode;
        }

        var items = rootItems?.ToList() ?? await BrowseDirectoryAsync("/");
        PopulateNodeChildren(rootNode, items);
        rootNode.IsExpanded = true;
        return rootNode;
    }

    private RemoteTreeNode GetOrCreateRootNode()
    {
        if (NavigationRoots.Count > 0)
        {
            return NavigationRoots[0];
        }

        var rootNode = new RemoteTreeNode
        {
            Name = "此电脑",
            FullPath = "/",
            IsDirectory = true,
            IsVirtual = true,
            ChildrenLoaded = false,
            IsExpanded = true
        };

        NavigationRoots.Add(rootNode);
        return rootNode;
    }

    private void ApplyDirectoryItems(string directoryPath, IEnumerable<ServerDirectoryItem> items)
    {
        CurrentServerDirectory = NormalizeDirectoryInput(directoryPath);
        _currentDirectoryItems.Clear();
        _currentDirectoryItems.AddRange(items
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase));

        if (PathsEqual(directoryPath, "/"))
        {
            _rootDisplaysDriveList = _currentDirectoryItems.Count > 0 && _currentDirectoryItems.All(item => item.IsDrive);
        }

        SelectedServerItem = null;
        RefreshVisibleItems();
    }

    private void PopulateNodeChildren(RemoteTreeNode node, IEnumerable<ServerDirectoryItem> items)
    {
        node.Children.Clear();
        foreach (var child in items.Where(item => item.IsDirectory)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            node.Children.Add(CreateTreeNode(child));
        }

        node.ChildrenLoaded = true;
    }

    private static RemoteTreeNode CreateTreeNode(ServerDirectoryItem item)
    {
        var node = new RemoteTreeNode
        {
            Name = item.Name,
            FullPath = item.FullPath,
            IsDirectory = item.IsDirectory,
            IsDrive = item.IsDrive
        };

        if (item.IsDirectory)
        {
            node.Children.Add(new RemoteTreeNode
            {
                Name = "加载中...",
                FullPath = item.FullPath,
                IsDirectory = false,
                IsPlaceholder = true
            });
        }

        return node;
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

    private void SelectVisibleItem(string fullPath)
    {
        SelectedServerItem = VisibleItems.FirstOrDefault(item =>
            PathsEqual(item.FullPath, fullPath));
    }

    private bool EnsureConnected()
    {
        if (_tcpHelper.IsRunning)
        {
            return true;
        }

        StatusText = "请先在顶部连接服务端。";
        return false;
    }

    private bool TryGetWritableTargetDirectory(ServerDirectoryItem? targetDirectory, out string remoteDirectory)
    {
        remoteDirectory = targetDirectory?.IsDirectory == true
            ? targetDirectory.FullPath
            : CurrentServerDirectory;

        if (!EnsureConnected())
        {
            return false;
        }

        if (!CanWriteToPath(remoteDirectory))
        {
            StatusText = "请先进入具体磁盘或目录后再执行此操作。";
            return false;
        }

        return true;
    }

    private bool CanWriteToPath(string path) => !_rootDisplaysDriveList || !PathsEqual(path, "/");

    private void ResetExplorer(string statusText)
    {
        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource?.Dispose();
        _searchCancellationTokenSource = null;

        FailPendingRequests("连接已断开。");

        NavigationRoots.Clear();
        VisibleItems.Clear();
        _currentDirectoryItems.Clear();
        _searchResults.Clear();
        SelectedTreeNode = null;
        SelectedServerItem = null;
        CurrentServerDirectory = "/";
        SearchKeyword = string.Empty;
        PendingDirectoryName = "新建文件夹";
        IsSearchMode = false;
        IsBusy = false;
        IsCreateDialogOpen = false;
        IsDeleteDialogOpen = false;
        StatusText = statusText;
    }

    private void FailPendingRequests(string message)
    {
        while (_pendingBrowseRequests.TryRemove(_pendingBrowseRequests.Keys.FirstOrDefault(), out var pendingBrowse))
        {
            pendingBrowse.CompletionSource.TrySetException(new InvalidOperationException(message));
        }

        while (_pendingCreateRequests.TryRemove(_pendingCreateRequests.Keys.FirstOrDefault(), out var pendingCreate))
        {
            pendingCreate.TrySetException(new InvalidOperationException(message));
        }

        while (_pendingDeleteRequests.TryRemove(_pendingDeleteRequests.Keys.FirstOrDefault(), out var pendingDelete))
        {
            pendingDelete.TrySetException(new InvalidOperationException(message));
        }
    }

    private static ServerDirectoryItem ToDirectoryItem(RemoteTreeNode node) => new()
    {
        Name = node.Name,
        FullPath = node.FullPath,
        IsDirectory = node.IsDirectory,
        IsDrive = node.IsDrive,
        Size = 0,
        LastModifiedTime = DateTime.MinValue
    };

    private static string NormalizeDirectoryInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/";
        }

        return value.Trim();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizeComparisonPath(left), NormalizeComparisonPath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeComparisonPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path is "/" or "\\")
        {
            return "/";
        }

        return path.Replace('\\', '/').Trim().TrimEnd('/');
    }

    private static string? GetDriveRootPath(string path)
    {
        var normalized = NormalizeDirectoryInput(path).Replace('/', '\\');
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            return normalized.Length >= 3 ? normalized[..3] : $"{normalized}\\";
        }

        return null;
    }

    private static IEnumerable<string> GetRelativeSegments(string path)
    {
        var normalized = NormalizeDirectoryInput(path).Replace('/', '\\');
        if (normalized.Length < 2 || normalized[1] != ':')
        {
            return [];
        }

        var remainder = normalized.Length > 3 ? normalized[3..] : string.Empty;
        return remainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static RemoteTreeNode? FindChildByPath(RemoteTreeNode parent, string path) =>
        parent.Children.FirstOrDefault(child => !child.IsPlaceholder && PathsEqual(child.FullPath, path));

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
            return normalized[..3];
        }

        return normalized[..lastSlash];
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
                IsDrive = true,
                Size = disk.TotalSize,
                LastModifiedTime = DateTime.MinValue
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
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
            IsDrive = false,
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
