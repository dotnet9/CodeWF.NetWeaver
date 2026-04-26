using CodeWF.EventBus;
using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Helpers;
using CodeWF.NetWrapper.Models;
using CodeWF.NetWrapper.Requests;
using CodeWF.NetWrapper.Response;
using ReactiveUI;
using SocketTest.Client.Features.RemoteFiles.Models;
using SocketTest.Client.Features.Transfers.ViewModels;
using SocketTest.Client.Shell.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SocketTest.Client.Features.RemoteFiles.ViewModels;

public class RemoteFileExplorerViewModel : ReactiveObject
{
    private readonly TcpSocketClient _tcpHelper;
    private readonly FileTransferViewModel _fileTransferViewModel;
    private readonly ConcurrentDictionary<int, PendingBrowseRequest> _pendingBrowseRequests = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<CreateDirectoryResponse>> _pendingCreateRequests = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<DeletePathResponse>> _pendingDeleteRequests = new();
    private readonly List<RemoteFileEntry> _currentDirectoryItems = [];
    private readonly List<RemoteFileEntry> _searchResults = [];
    private string _currentDirectoryPath = "/";
    private string _searchKeyword = string.Empty;
    private bool _searchRecursive = true;
    private bool _isListView = true;
    private bool _isBusy;
    private bool _isSearchMode;
    private string _statusMessage = "请先连接到服务端。";
    private int _currentSearchPage = 1;
    private int _searchPageSize = 40;
    private bool _isCreateDialogOpen;
    private string _pendingDirectoryName = "新建文件夹";
    private string _createDialogHint = string.Empty;
    private string _createDirectoryBasePath = "/";
    private bool _isDeleteDialogOpen;
    private string _deleteDialogMessage = string.Empty;
    private RemoteFileEntry? _selectedEntry;
    private RemoteFileEntry? _pendingDeleteEntry;
    private RemoteDirectoryNode? _selectedNode;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private bool _rootDisplaysDriveList = true;

    public RemoteFileExplorerViewModel(TcpSocketClient tcpHelper, FileTransferViewModel fileTransferViewModel)
    {
        _tcpHelper = tcpHelper;
        _fileTransferViewModel = fileTransferViewModel;

        RootNodes = [];
        VisibleEntries = [];

        EventBus.Default.Subscribe(this);
    }

    public ObservableCollection<RemoteDirectoryNode> RootNodes { get; }

    public ObservableCollection<RemoteFileEntry> VisibleEntries { get; }

    public RemoteDirectoryNode? SelectedNode
    {
        get => _selectedNode;
        private set => this.RaiseAndSetIfChanged(ref _selectedNode, value);
    }

    public RemoteFileEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedEntry, value);
    }

    public string CurrentDirectoryPath
    {
        get => _currentDirectoryPath;
        set => this.RaiseAndSetIfChanged(ref _currentDirectoryPath, NormalizeDirectoryInput(value));
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
            this.RaisePropertyChanged(nameof(ExplorerSummary));
            this.RaisePropertyChanged(nameof(IsBrowseMode));
        }
    }

    public bool IsBrowseMode => !IsSearchMode;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public int CurrentSearchPage
    {
        get => _currentSearchPage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentSearchPage, value);
            this.RaisePropertyChanged(nameof(ExplorerSummary));
            this.RaisePropertyChanged(nameof(CanGoPreviousSearchPage));
            this.RaisePropertyChanged(nameof(CanGoNextSearchPage));
        }
    }

    public int SearchPageSize
    {
        get => _searchPageSize;
        set
        {
            var pageSize = Math.Max(10, value);
            this.RaiseAndSetIfChanged(ref _searchPageSize, pageSize);
            RefreshVisibleEntries();
        }
    }

    public int SearchTotalPages => Math.Max(1, (int)Math.Ceiling(_searchResults.Count / (double)SearchPageSize));

    public bool CanGoPreviousSearchPage => CurrentSearchPage > 1;

    public bool CanGoNextSearchPage => CurrentSearchPage < SearchTotalPages;

    public string ExplorerSummary => IsSearchMode
        ? $"搜索结果 {_searchResults.Count} 项，页码 {CurrentSearchPage}/{SearchTotalPages}"
        : $"当前目录项 {_currentDirectoryItems.Count} 个";

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

    public Task RefreshDirectoryCommand() => RefreshCurrentDirectoryAsync();

    public Task GoToDirectoryCommand() => GoToCurrentDirectoryAsync();

    public Task EnterParentDirectoryCommand() => EnterParentDirectoryAsync();

    public Task OpenEntryCommand(RemoteFileEntry entry) => OpenEntryAsync(entry);

    public Task StartSearchCommand() => StartSearchAsync();

    public void ClearSearchCommand() => ClearSearch();

    public void PreviousSearchPageCommand() => GoToPreviousSearchPage();

    public void NextSearchPageCommand() => GoToNextSearchPage();

    public void SwitchToListViewCommand() => IsListView = true;

    public void SwitchToTileViewCommand() => IsListView = false;

    public void ShowCreateDirectoryDialogCommand(RemoteFileEntry? entry) => ShowCreateDirectoryDialog(entry);

    public Task ConfirmCreateDirectoryCommand() => ConfirmCreateDirectoryAsync();

    public void CancelCreateDirectoryCommand() => CancelCreateDirectoryDialog();

    public void ShowDeleteDialogCommand(RemoteFileEntry? entry) => ShowDeleteDialog(entry);

    public Task ConfirmDeleteCommand() => ConfirmDeleteAsync();

    public void CancelDeleteCommand() => CancelDeleteDialog();

    /// <summary>
    /// 连接建立后自动初始化浏览器，连接断开时则统一清理待完成请求和当前界面状态。
    /// </summary>
    public async Task HandleConnectionStateChangedAsync(bool isConnected)
    {
        if (isConnected)
        {
            await InitializeExplorerAsync();
            return;
        }

        ResetExplorer("连接已断开，请重新连接服务端。");
    }

    /// <summary>
    /// 初始化远程文件浏览器，并确保首次展示的根目录和目录树来源一致。
    /// </summary>
    public async Task InitializeExplorerAsync()
    {
        if (!EnsureConnected())
        {
            return;
        }

        try
        {
            await RunBusyAsync(async () =>
            {
                ClearSearchInternal();

                var rootNode = GetOrCreateRootNode();
                var rootItems = await BrowseDirectoryAsync("/");

                ApplyDirectoryItems("/", rootItems);
                PopulateNodeChildren(rootNode, rootItems);
                ActivateNode(rootNode);
                SelectedNode = rootNode;
                StatusMessage = "已连接服务端，正在浏览远程文件系统。";
            });
        }
        catch (Exception ex)
        {
            Logger.Error("初始化远程文件浏览器失败", ex);
            ResetExplorer(ex.Message);
        }
    }

    public async Task SelectNodeAsync(RemoteDirectoryNode? node)
    {
        if (node == null || node.IsPlaceholder || !EnsureConnected())
        {
            return;
        }

        SelectedNode = node;
        node.IsSelected = true;
        await NavigateToPathAsync(node.FullPath);
    }

    public async Task ExpandNodeAsync(RemoteDirectoryNode? node)
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
            var items = PathsEqual(node.FullPath, CurrentDirectoryPath) && !IsSearchMode
                ? _currentDirectoryItems.ToList()
                : await BrowseDirectoryAsync(node.FullPath);

            PopulateNodeChildren(node, items);
            node.IsExpanded = true;
        }
        catch (Exception ex)
        {
            Logger.Error($"加载目录树节点失败：{node.FullPath}", ex);
            StatusMessage = ex.Message;
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    public async Task UploadFilesAsync(IEnumerable<string> localFilePaths, RemoteFileEntry? targetDirectory = null)
    {
        if (!TryGetWritableTargetDirectory(targetDirectory, out var remoteDirectory))
        {
            return;
        }

        var files = localFilePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            StatusMessage = "没有可上传的文件。";
            return;
        }

        var transfers = files.Select(file => (file, CombineRemotePath(remoteDirectory, Path.GetFileName(file)))).ToList();
        _fileTransferViewModel.EnqueueUploads(transfers);
        StatusMessage = $"已加入 {transfers.Count} 个上传任务。";
    }

    public void ShowCreateDirectoryDialogFor(RemoteFileEntry? entry) => ShowCreateDirectoryDialog(entry);

    public void ShowCreateDirectoryDialogForNode(RemoteDirectoryNode? node) => ShowCreateDirectoryDialog(ToEntryOrNull(node));

    public void ShowDeleteDialogFor(RemoteFileEntry? entry) => ShowDeleteDialog(entry);

    public void ShowDeleteDialogForNode(RemoteDirectoryNode? node) => ShowDeleteDialog(ToEntryOrNull(node));

    public Task OpenEntryFromMenuAsync(RemoteFileEntry entry) => OpenEntryAsync(entry);

    public async Task UploadFolderAsync(string localFolderPath, RemoteFileEntry? targetDirectory = null)
    {
        if (!TryGetWritableTargetDirectory(targetDirectory, out var remoteDirectory))
        {
            return;
        }

        if (!Directory.Exists(localFolderPath))
        {
            StatusMessage = "所选文件夹不存在。";
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
            StatusMessage = ex.Message;
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
        StatusMessage = $"已加入文件夹上传任务，共 {uploads.Count} 个文件。";
    }

    public async Task DownloadEntryAsync(RemoteFileEntry? entry, string localRootDirectory)
    {
        if (!EnsureConnected())
        {
            return;
        }

        if (entry == null)
        {
            StatusMessage = "请先选择要下载的文件或文件夹。";
            return;
        }

        if (entry.IsDrive)
        {
            StatusMessage = "请先进入具体磁盘或目录后再下载。";
            return;
        }

        Directory.CreateDirectory(localRootDirectory);

        if (!entry.IsDirectory)
        {
            _fileTransferViewModel.EnqueueDownloads((entry.FullPath, Path.Combine(localRootDirectory, entry.Name)));
            StatusMessage = $"已加入下载任务：{entry.Name}";
            return;
        }

        var downloads = new List<(string RemotePath, string LocalPath)>();
        var baseLocalDirectory = Path.Combine(localRootDirectory, entry.Name);
        Directory.CreateDirectory(baseLocalDirectory);

        var queue = new Queue<(string RemotePath, string RelativePath)>();
        queue.Enqueue((entry.FullPath, string.Empty));

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
        StatusMessage = $"已加入文件夹下载任务，共 {downloads.Count} 个文件。";
    }

    public Task RefreshCurrentDirectoryAsync() => NavigateToPathAsync(CurrentDirectoryPath);

    public Task GoToCurrentDirectoryAsync() => NavigateToPathAsync(CurrentDirectoryPath);

    public async Task EnterParentDirectoryAsync()
    {
        var parent = GetParentRemotePath(CurrentDirectoryPath);
        if (!PathsEqual(parent, CurrentDirectoryPath))
        {
            await NavigateToPathAsync(parent);
        }
    }

    public async Task NavigateToPathAsync(string directoryPath, string? selectItemFullPath = null)
    {
        if (!EnsureConnected())
        {
            return;
        }

        try
        {
            await RunBusyAsync(async () =>
            {
                var normalizedPath = NormalizeDirectoryInput(directoryPath);
                ClearSearchInternal();

                var items = await BrowseDirectoryAsync(normalizedPath);
                ApplyDirectoryItems(normalizedPath, items);

                var matchedNode = await EnsureNodeForPathAsync(normalizedPath, items);
                ActivateNode(matchedNode);
                SelectedNode = matchedNode;

                if (!string.IsNullOrWhiteSpace(selectItemFullPath))
                {
                    SelectVisibleEntry(selectItemFullPath);
                }

                StatusMessage = $"已打开目录：{CurrentDirectoryPath}";
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开目录失败：{directoryPath}", ex);
            StatusMessage = ex.Message;
        }
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

        IsSearchMode = true;
        _searchResults.Clear();
        CurrentSearchPage = 1;
        RefreshVisibleEntries();

        try
        {
            await RunBusyAsync(async () =>
            {
                var queue = new Queue<string>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                queue.Enqueue(CurrentDirectoryPath);

                while (queue.Count > 0)
                {
                    _searchCancellationTokenSource.Token.ThrowIfCancellationRequested();

                    var remotePath = queue.Dequeue();
                    if (!visited.Add(NormalizeComparisonPath(remotePath)))
                    {
                        continue;
                    }

                    StatusMessage = $"正在搜索，已扫描 {visited.Count} 个目录...";
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

                    RefreshVisibleEntries();
                }

                StatusMessage = $"搜索完成，共找到 {_searchResults.Count} 个匹配项。";
            }, RefreshVisibleEntries);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "搜索已取消。";
        }
        catch (Exception ex)
        {
            Logger.Error("远程搜索失败", ex);
            StatusMessage = ex.Message;
        }
    }

    /// <summary>
    /// 统一分发文件浏览相关响应，保证分页目录查询与创建/删除确认都走同一条日志链路。
    /// </summary>
    public void ReceivedSocketCommand(SocketCommand message)
    {
        if (message.IsCommand<BrowseFileSystemResponse>())
        {
            var response = message.GetCommand<BrowseFileSystemResponse>();
            LogIncomingResponse(response);
            HandleBrowseResponse(response);
        }
        else if (message.IsCommand<DriveListResponse>())
        {
            var response = message.GetCommand<DriveListResponse>();
            LogIncomingResponse(response);
            HandleDriveListResponse(response);
        }
        else if (message.IsCommand<CreateDirectoryResponse>())
        {
            var response = message.GetCommand<CreateDirectoryResponse>();
            LogIncomingResponse(response);
            HandleCreateDirectoryResponse(response);
        }
        else if (message.IsCommand<DeletePathResponse>())
        {
            var response = message.GetCommand<DeletePathResponse>();
            LogIncomingResponse(response);
            HandleDeletePathResponse(response);
        }
        else if (message.IsCommand<FileTransferReject>())
        {
            var response = message.GetCommand<FileTransferReject>();
            LogIncomingResponse(response);
            HandleRemoteReject(response);
        }
    }

    [EventHandler]
    private async Task ReceiveClientConnectionStateChangedAsync(ClientConnectionStateChangedMessage message)
    {
        await HandleConnectionStateChangedAsync(message.IsConnected);
    }

    private async Task OpenEntryAsync(RemoteFileEntry entry)
    {
        if (entry.IsDirectory)
        {
            await NavigateToPathAsync(entry.FullPath);
            return;
        }

        if (IsSearchMode)
        {
            await NavigateToPathAsync(GetParentRemotePath(entry.FullPath), entry.FullPath);
            return;
        }

        SelectedEntry = entry;
        StatusMessage = $"已选中文件：{entry.Name}。可通过菜单或工具栏执行下载。";
    }

    private void ClearSearch()
    {
        _searchCancellationTokenSource?.Cancel();
        ClearSearchInternal();
        RefreshVisibleEntries();
        StatusMessage = $"已返回目录浏览：{CurrentDirectoryPath}";
    }

    private void ClearSearchInternal()
    {
        IsSearchMode = false;
        _searchResults.Clear();
        CurrentSearchPage = 1;
    }

    private void GoToPreviousSearchPage()
    {
        if (CanGoPreviousSearchPage)
        {
            CurrentSearchPage--;
            RefreshVisibleEntries();
        }
    }

    private void GoToNextSearchPage()
    {
        if (CanGoNextSearchPage)
        {
            CurrentSearchPage++;
            RefreshVisibleEntries();
        }
    }

    private void ShowCreateDirectoryDialog(RemoteFileEntry? targetEntry)
    {
        var targetPath = targetEntry?.IsDirectory == true
            ? targetEntry.FullPath
            : CurrentDirectoryPath;

        if (!CanWriteToPath(targetPath))
        {
            StatusMessage = "请先进入具体磁盘或目录后再创建文件夹。";
            return;
        }

        _createDirectoryBasePath = targetPath;
        CreateDialogHint = $"将在 {_createDirectoryBasePath} 中创建文件夹。";
        PendingDirectoryName = "新建文件夹";
        IsCreateDialogOpen = true;
    }

    private async Task ConfirmCreateDirectoryAsync()
    {
        var directoryName = PendingDirectoryName.Trim();
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            StatusMessage = "目录名称不能为空。";
            return;
        }

        IsCreateDialogOpen = false;
        try
        {
            var remotePath = CombineRemotePath(_createDirectoryBasePath, directoryName);
            await CreateDirectoryRequestAsync(remotePath);
            await NavigateToPathAsync(_createDirectoryBasePath);
            StatusMessage = $"已创建目录：{remotePath}";
        }
        catch (Exception ex)
        {
            Logger.Error("创建远程目录失败", ex);
            StatusMessage = ex.Message;
        }
    }

    private void CancelCreateDirectoryDialog() => IsCreateDialogOpen = false;

    private void ShowDeleteDialog(RemoteFileEntry? entry)
    {
        _pendingDeleteEntry = entry ?? SelectedEntry;
        if (_pendingDeleteEntry == null)
        {
            StatusMessage = "请先选择要删除的文件或文件夹。";
            return;
        }

        if (_pendingDeleteEntry.IsDrive)
        {
            StatusMessage = "不支持删除磁盘根节点。";
            _pendingDeleteEntry = null;
            return;
        }

        DeleteDialogMessage = _pendingDeleteEntry.IsDirectory
            ? $"确认删除文件夹“{_pendingDeleteEntry.Name}”吗？目录必须为空。"
            : $"确认删除文件“{_pendingDeleteEntry.Name}”吗？";
        IsDeleteDialogOpen = true;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteEntry == null)
        {
            IsDeleteDialogOpen = false;
            return;
        }

        var entry = _pendingDeleteEntry;
        _pendingDeleteEntry = null;
        IsDeleteDialogOpen = false;

        try
        {
            await DeletePathRequestAsync(entry.FullPath, entry.IsDirectory);

            var refreshPath = entry.IsDirectory && PathsEqual(entry.FullPath, CurrentDirectoryPath)
                ? GetParentRemotePath(entry.FullPath)
                : CurrentDirectoryPath;

            await NavigateToPathAsync(refreshPath);
            StatusMessage = $"已删除：{entry.FullPath}";
        }
        catch (Exception ex)
        {
            Logger.Error("删除远程路径失败", ex);
            StatusMessage = ex.Message;
        }
    }

    private void CancelDeleteDialog()
    {
        _pendingDeleteEntry = null;
        IsDeleteDialogOpen = false;
    }

    /// <summary>
    /// 发送目录浏览请求，并在分页响应全部到齐后再返回聚合后的目录项。
    /// </summary>
    private async Task<List<RemoteFileEntry>> BrowseDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var normalizedRequestPath = PathsEqual(directoryPath, "/") ? string.Empty : directoryPath;
        var taskId = NetHelper.GetTaskId();
        var completionSource = new TaskCompletionSource<List<RemoteFileEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingBrowseRequest(directoryPath, completionSource);
        _pendingBrowseRequests[taskId] = request;

        using var registration = cancellationToken.Register(() =>
        {
            if (_pendingBrowseRequests.TryRemove(taskId, out var pending))
            {
                pending.CompletionSource.TrySetCanceled(cancellationToken);
            }
        });

        await SendTcpRequestAsync(new BrowseFileSystemRequest
        {
            TaskId = taskId,
            DirectoryPath = normalizedRequestPath
        });

        return await completionSource.Task;
    }

    /// <summary>
    /// 发送创建目录请求，并把返回结果统一转换为成功值或异常。
    /// </summary>
    private async Task CreateDirectoryRequestAsync(string directoryPath)
    {
        var response = await SendRequestAsync(
            _pendingCreateRequests,
            taskId => SendTcpRequestAsync(new CreateDirectoryRequest
            {
                TaskId = taskId,
                DirectoryPath = directoryPath
            }));

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }
    }

    /// <summary>
    /// 发送删除路径请求，并复用统一的请求-应答桥接逻辑处理最终确认对象。
    /// </summary>
    private async Task DeletePathRequestAsync(string filePath, bool isDirectory)
    {
        var response = await SendRequestAsync(
            _pendingDeleteRequests,
            taskId => SendTcpRequestAsync(new DeletePathRequest
            {
                TaskId = taskId,
                FilePath = filePath,
                IsDirectory = isDirectory
            }));

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }
    }

    private async Task<RemoteDirectoryNode?> EnsureNodeForPathAsync(string directoryPath, IReadOnlyCollection<RemoteFileEntry>? currentItems = null)
    {
        var normalizedPath = NormalizeDirectoryInput(directoryPath);
        var rootNode = await EnsureRootNodeLoadedAsync(PathsEqual(normalizedPath, "/") ? currentItems : null);

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
                await ExpandNodeAsync(currentNode);
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

    private async Task<RemoteDirectoryNode> EnsureRootNodeLoadedAsync(IReadOnlyCollection<RemoteFileEntry>? rootItems = null)
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

    private RemoteDirectoryNode GetOrCreateRootNode()
    {
        if (RootNodes.Count > 0)
        {
            return RootNodes[0];
        }

        var rootNode = new RemoteDirectoryNode
        {
            Name = "此电脑",
            FullPath = "/",
            IsDirectory = true,
            IsVirtual = true,
            ChildrenLoaded = false,
            IsExpanded = true
        };

        RootNodes.Add(rootNode);
        return rootNode;
    }

    private void ApplyDirectoryItems(string directoryPath, IEnumerable<RemoteFileEntry> items)
    {
        CurrentDirectoryPath = NormalizeDirectoryInput(directoryPath);
        _currentDirectoryItems.Clear();
        _currentDirectoryItems.AddRange(items
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase));

        if (PathsEqual(directoryPath, "/"))
        {
            _rootDisplaysDriveList = _currentDirectoryItems.Count > 0 && _currentDirectoryItems.All(item => item.IsDrive);
        }

        SelectedEntry = null;
        RefreshVisibleEntries();
    }

    private void PopulateNodeChildren(RemoteDirectoryNode node, IEnumerable<RemoteFileEntry> items)
    {
        node.Children.Clear();
        foreach (var child in items.Where(item => item.IsDirectory).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            node.Children.Add(CreateNode(child));
        }

        node.ChildrenLoaded = true;
    }

    private static RemoteDirectoryNode CreateNode(RemoteFileEntry entry)
    {
        var node = new RemoteDirectoryNode
        {
            Name = entry.Name,
            FullPath = entry.FullPath,
            IsDirectory = entry.IsDirectory,
            IsDrive = entry.IsDrive
        };

        if (entry.IsDirectory)
        {
            node.Children.Add(new RemoteDirectoryNode
            {
                Name = "加载中...",
                FullPath = entry.FullPath,
                IsDirectory = false,
                IsPlaceholder = true
            });
        }

        return node;
    }

    private void RefreshVisibleEntries()
    {
        VisibleEntries.Clear();

        var source = IsSearchMode
            ? _searchResults.Skip((CurrentSearchPage - 1) * SearchPageSize).Take(SearchPageSize)
            : _currentDirectoryItems;

        foreach (var entry in source)
        {
            VisibleEntries.Add(entry);
        }

        this.RaisePropertyChanged(nameof(ExplorerSummary));
        this.RaisePropertyChanged(nameof(CanGoPreviousSearchPage));
        this.RaisePropertyChanged(nameof(CanGoNextSearchPage));
    }

    private void SelectVisibleEntry(string fullPath)
    {
        SelectedEntry = VisibleEntries.FirstOrDefault(item => PathsEqual(item.FullPath, fullPath));
    }

    private bool EnsureConnected()
    {
        if (_tcpHelper.IsRunning)
        {
            return true;
        }

        StatusMessage = "请先连接到服务端。";
        return false;
    }

    private bool TryGetWritableTargetDirectory(RemoteFileEntry? targetDirectory, out string remoteDirectory)
    {
        remoteDirectory = targetDirectory?.IsDirectory == true
            ? targetDirectory.FullPath
            : CurrentDirectoryPath;

        if (!EnsureConnected())
        {
            return false;
        }

        if (!CanWriteToPath(remoteDirectory))
        {
            StatusMessage = "请先进入具体磁盘或目录后再执行此操作。";
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

        RootNodes.Clear();
        VisibleEntries.Clear();
        _currentDirectoryItems.Clear();
        _searchResults.Clear();
        SelectedNode = null;
        SelectedEntry = null;
        CurrentDirectoryPath = "/";
        SearchKeyword = string.Empty;
        PendingDirectoryName = "新建文件夹";
        IsSearchMode = false;
        IsBusy = false;
        IsCreateDialogOpen = false;
        IsDeleteDialogOpen = false;
        StatusMessage = statusText;
    }

    private void FailPendingRequests(string message)
    {
        FailPendingRequests(_pendingBrowseRequests, pendingBrowse =>
            pendingBrowse.CompletionSource.TrySetException(new InvalidOperationException(message)));
        FailPendingRequests(_pendingCreateRequests, pendingCreate =>
            pendingCreate.TrySetException(new InvalidOperationException(message)));
        FailPendingRequests(_pendingDeleteRequests, pendingDelete =>
            pendingDelete.TrySetException(new InvalidOperationException(message)));
    }

    private static RemoteFileEntry ToEntry(RemoteDirectoryNode node) => new()
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

    private static RemoteDirectoryNode? FindChildByPath(RemoteDirectoryNode parent, string path) =>
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
                    pendingRequest.Items.Add(ToRemoteFileEntry(pendingRequest.RequestedPath, entry));
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
            .Select(disk => new RemoteFileEntry
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
        TrySetPendingResult(_pendingCreateRequests, response.TaskId, response);
    }

    private void HandleDeletePathResponse(DeletePathResponse response)
    {
        TrySetPendingResult(_pendingDeleteRequests, response.TaskId, response);
    }

    private void HandleRemoteReject(FileTransferReject reject)
    {
        if (_pendingBrowseRequests.TryRemove(reject.TaskId, out var browseRequest))
        {
            browseRequest.CompletionSource.TrySetException(new InvalidOperationException(reject.Message));
            return;
        }

        var exception = new InvalidOperationException(reject.Message);
        if (TrySetPendingException(_pendingCreateRequests, reject.TaskId, exception))
        {
            return;
        }

        TrySetPendingException(_pendingDeleteRequests, reject.TaskId, exception);
    }

    private static RemoteFileEntry ToRemoteFileEntry(string requestedPath, FileSystemEntry entry)
    {
        var normalizedBasePath = string.IsNullOrWhiteSpace(requestedPath) ? "/" : requestedPath;
        return new RemoteFileEntry
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

    private async Task RunBusyAsync(Func<Task> action, Action? finallyAction = null)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
            finallyAction?.Invoke();
        }
    }

    /// <summary>
    /// 为一次 TCP 请求建立 TaskCompletionSource，把“异步发送 + 后续响应”桥接成可 await 的任务。
    /// </summary>
    private async Task<TResponse> SendRequestAsync<TResponse>(
        ConcurrentDictionary<int, TaskCompletionSource<TResponse>> pendingRequests,
        Func<int, Task> sendRequestAsync)
    {
        var taskId = NetHelper.GetTaskId();
        var completionSource = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[taskId] = completionSource;

        await sendRequestAsync(taskId);
        return await completionSource.Task;
    }

    private async Task SendTcpRequestAsync(CodeWF.NetWeaver.Base.INetObject request)
    {
        Logger.Info($"客户端 -> 服务端 文件 TCP：{DescribeMessage(request)}");
        await _tcpHelper.SendCommandAsync(request);
    }

    private static void LogIncomingResponse(object response)
    {
        if (response is BrowseFileSystemResponse browse && browse.PageIndex > 0)
        {
            return;
        }

        Logger.Info($"服务端 -> 客户端 文件 TCP：{DescribeMessage(response)}");
    }

    private static string DescribeMessage(object message) =>
        message switch
        {
            BrowseFileSystemRequest request => $"请求浏览文件系统(TaskId={request.TaskId},路径={request.DirectoryPath})",
            CreateDirectoryRequest request => $"请求创建目录(TaskId={request.TaskId},路径={request.DirectoryPath})",
            DeletePathRequest request => $"请求删除路径(TaskId={request.TaskId},路径={request.FilePath},目录={request.IsDirectory})",
            BrowseFileSystemResponse response => $"返回浏览文件系统(TaskId={response.TaskId},页={response.PageIndex + 1}/{response.PageCount},条目数={response.Entries?.Count ?? 0})",
            DriveListResponse response => $"返回磁盘列表(TaskId={response.TaskId},磁盘数={response.Disks?.Count ?? 0})",
            CreateDirectoryResponse response => $"返回创建目录结果(TaskId={response.TaskId},成功={response.Success},路径={response.DirectoryPath})",
            DeletePathResponse response => $"返回删除路径结果(TaskId={response.TaskId},成功={response.Success},路径={response.FilePath})",
            FileTransferReject response => $"文件传输拒绝(TaskId={response.TaskId},错误码={response.ErrorCode},路径={response.RemoteFilePath})",
            _ => message.GetType().Name
        };

    private static void ActivateNode(RemoteDirectoryNode? node)
    {
        if (node == null)
        {
            return;
        }

        node.IsExpanded = true;
        node.IsSelected = true;
    }

    private static void FailPendingRequests<TPending>(
        ConcurrentDictionary<int, TPending> pendingRequests,
        Action<TPending> failPendingRequest)
    {
        foreach (var pendingRequest in pendingRequests.ToArray())
        {
            if (pendingRequests.TryRemove(pendingRequest.Key, out var pending))
            {
                failPendingRequest(pending);
            }
        }
    }

    private static bool TrySetPendingResult<TResponse>(
        ConcurrentDictionary<int, TaskCompletionSource<TResponse>> pendingRequests,
        int taskId,
        TResponse response)
    {
        return pendingRequests.TryRemove(taskId, out var completionSource) &&
               completionSource.TrySetResult(response);
    }

    private static bool TrySetPendingException<TResponse>(
        ConcurrentDictionary<int, TaskCompletionSource<TResponse>> pendingRequests,
        int taskId,
        Exception exception)
    {
        return pendingRequests.TryRemove(taskId, out var completionSource) &&
               completionSource.TrySetException(exception);
    }

    private static RemoteFileEntry? ToEntryOrNull(RemoteDirectoryNode? node) =>
        node == null || node.IsPlaceholder ? null : ToEntry(node);

    private sealed class PendingBrowseRequest
    {
        public PendingBrowseRequest(string requestedPath, TaskCompletionSource<List<RemoteFileEntry>> completionSource)
        {
            RequestedPath = requestedPath;
            CompletionSource = completionSource;
        }

        public string RequestedPath { get; }

        public TaskCompletionSource<List<RemoteFileEntry>> CompletionSource { get; }

        public List<RemoteFileEntry> Items { get; } = [];

        public int ExpectedPages { get; set; }

        public int ReceivedPages { get; set; }

        public object SyncRoot { get; } = new();
    }
}
