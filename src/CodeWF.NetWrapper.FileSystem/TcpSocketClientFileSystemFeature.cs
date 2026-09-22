namespace CodeWF.NetWrapper.FileSystem;

public sealed class TcpSocketClientFileSystemFeature : IDisposable
{
    private readonly TcpSocketClient _client;
    private readonly IDisposable _registration;

    /// <summary>
    ///     文件传输块大小（64KB），每次传输的数据块大小
    /// </summary>
    public const int FileTransferBlockSize = 64 * 1024;

    private readonly ConcurrentDictionary<string, DownloadContext> _downloadContexts = new();

    private readonly ConcurrentDictionary<string, UploadContext> _uploadContexts = new();
    public TcpSocketClientFileSystemFeature(TcpSocketClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _registration = client.RegisterCommandHandler(TryHandleCommandAsync);
    }

    public void Dispose()
    {
        _registration.Dispose();
        _uploadContexts.Clear();
        _downloadContexts.Clear();
    }

    /// <summary>
    ///     文件传输进度事件，当文件传输进度更新时触发
    /// </summary>
    public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgress;

    /// <summary>
    ///     文件传输结果事件，当文件完成、失败或被取消时触发。
    /// </summary>
    public event EventHandler<FileTransferOutcomeEventArgs>? FileTransferOutcome;

    /// <summary>
    ///     浏览服务端文件系统。
    /// </summary>
    /// <param name="serverDirectoryPath">服务端目录路径；为空时由服务端决定返回根目录或磁盘列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task BrowseFileSystemAsync(string serverDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (!_client.CanSend)
        {
            Logger.Error($"{_client.ServerMark} 未连接，无法查询服务端目录");
            return;
        }

        var taskId = NetHelper.GetTaskId();
        var queryCommand = new BrowseFileSystemRequest
        {
            TaskId = taskId,
            DirectoryPath = serverDirectoryPath
        };

        await _client.SendCommandAsync(queryCommand, cancellationToken);
        Logger.Info($"{_client.ServerMark} 请求查询服务端目录：{serverDirectoryPath}");
    }

    /// <summary>
    ///     在服务端创建目录。
    /// </summary>
    /// <param name="serverDirectoryPath">服务端目录路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task CreateDirectoryAsync(string serverDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (!_client.CanSend)
        {
            Logger.Error($"{_client.ServerMark} 未连接，无法创建服务端目录");
            return;
        }

        var taskId = NetHelper.GetTaskId();
        var createCommand = new CreateDirectoryRequest
        {
            TaskId = taskId,
            DirectoryPath = serverDirectoryPath
        };

        await _client.SendCommandAsync(createCommand, cancellationToken);
        Logger.Info($"{_client.ServerMark} 请求创建服务端目录：{serverDirectoryPath}");
    }

    /// <summary>
    ///     删除服务端路径。
    /// </summary>
    /// <param name="serverPath">服务端文件或目录路径。</param>
    /// <param name="isDirectory">是否按目录删除。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task DeletePathAsync(string serverPath, bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!_client.CanSend)
        {
            Logger.Error($"{_client.ServerMark} 未连接，无法删除服务端路径");
            return;
        }

        var taskId = NetHelper.GetTaskId();
        var deleteCommand = new DeletePathRequest
        {
            TaskId = taskId,
            FilePath = serverPath,
            IsDirectory = isDirectory
        };

        await _client.SendCommandAsync(deleteCommand, cancellationToken);
        Logger.Info($"{_client.ServerMark} 请求删除服务端路径：{serverPath}，IsDirectory={isDirectory}");
    }

    /// <summary>
    ///     向服务端上传文件。
    /// </summary>
    /// <param name="localFilePath">本地文件路径。</param>
    /// <param name="remoteFilePath">服务端保存路径（包含文件名）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task UploadFileAsync(string localFilePath, string remoteFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!_client.CanSend)
        {
            Logger.Error($"{_client.ServerMark} 未连接，无法开始文件上传");
            return;
        }

        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            Logger.Error($"{_client.ServerMark} 文件不存在：{localFilePath}");
            return;
        }

        // Path.GetFileName 用于从完整路径中提取最终文件名，避免把本地目录结构直接带到协议字段里。
        var remoteFileName = Path.GetFileName(remoteFilePath);
        var fileHash = await ComputeFileHashAsync(localFilePath, cancellationToken);
        var taskId = NetHelper.GetTaskId();

        var context = new UploadContext
        {
            TaskId = taskId,
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            LocalFilePath = localFilePath,
            RemoteFilePath = remoteFilePath,
            FileHash = fileHash,
            AlreadyTransferredBytes = 0,
            CancellationToken = cancellationToken
        };
        _uploadContexts[ClientTransferContextKeys.GetTransferKey(remoteFilePath, taskId)] = context;

        var startCommand = new FileUploadRequest
        {
            TaskId = taskId,
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            FileHash = fileHash,
            AlreadyTransferredBytes = 0,
            RemoteFilePath = remoteFilePath
        };

        await _client.SendCommandAsync(startCommand, cancellationToken);
        Logger.Info($"{_client.ServerMark} 请求上传文件：{localFilePath} -> {remoteFilePath}，大小：{fileInfo.Length}字节，等待服务端响应...");
    }

    /// <summary>
    ///     从服务端下载文件。
    /// </summary>
    /// <param name="serverFilePath">服务端待下载文件完整路径。</param>
    /// <param name="localSaveDirectory">本地保存目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task DownloadFileAsync(string serverFilePath, string localSaveDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!_client.CanSend)
        {
            Logger.Error($"{_client.ServerMark} 未连接，无法开始文件下载");
            return;
        }

        var serverFileName = Path.GetFileName(serverFilePath);
        var taskId = NetHelper.GetTaskId();
        if (!Directory.Exists(localSaveDirectory))
        {
            Directory.CreateDirectory(localSaveDirectory);
        }

        var localSavePath = Path.Combine(localSaveDirectory, serverFileName);
        var tempFilePath = GetPartFilePath(localSavePath);
        if (!File.Exists(tempFilePath) && File.Exists(localSavePath))
        {
            File.Copy(localSavePath, tempFilePath, true);
        }

        var alreadyTransferredBytes = File.Exists(tempFilePath)
            ? new FileInfo(tempFilePath).Length
            : GetExistingTransferBytes(tempFilePath);
        var localFileHash = alreadyTransferredBytes > 0
            ? await ComputeFileHashAsync(tempFilePath, cancellationToken)
            : string.Empty;

        var context = new DownloadContext
        {
            TaskId = taskId,
            FileName = serverFileName,
            FileSize = 0,
            FileHash = localFileHash,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            LocalFilePath = localSavePath,
            TempFilePath = tempFilePath,
            RemoteFilePath = serverFilePath,
            CancellationToken = cancellationToken
        };
        _downloadContexts[ClientTransferContextKeys.GetTransferKey(serverFilePath, taskId)] = context;

        var startCommand = new FileDownloadRequest
        {
            TaskId = taskId,
            FileName = serverFileName,
            FileSize = 0,
            FileHash = localFileHash,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            RemoteFilePath = serverFilePath
        };

        await _client.SendCommandAsync(startCommand, cancellationToken);
        Logger.Info($"{_client.ServerMark} 请求下载文件：{serverFilePath} -> {localSaveDirectory}，已传输：{alreadyTransferredBytes}字节");
    }

    /// <summary>
    ///     尝试处理文件系统扩展命令。
    /// </summary>
    internal async Task<bool> TryHandleCommandAsync(SocketCommand command)
    {
        try
        {
            if (command.IsCommand<FileUploadResponse>())
            {
                var response = command.GetCommand<FileUploadResponse>();
                await HandleFileUploadResponseAsync(response);
                return true;
            }

            if (command.IsCommand<FileChunkAck>())
            {
                var chunkAck = command.GetCommand<FileChunkAck>();
                await HandleFileChunkAckAsync(chunkAck);
                return true;
            }

            if (command.IsCommand<FileDownloadRequest>())
            {
                var request = command.GetCommand<FileDownloadRequest>();
                await HandleFileDownloadRequestAsync(request);
                return true;
            }

            if (command.IsCommand<FileDownloadResponse>())
            {
                var response = command.GetCommand<FileDownloadResponse>();
                await HandleFileDownloadResponseAsync(response);
                return true;
            }

            if (command.IsCommand<FileChunkData>())
            {
                var chunkData = command.GetCommand<FileChunkData>();
                await HandleFileChunkDataAsync(chunkData);
                return true;
            }

            if (command.IsCommand<FileTransferReject>())
            {
                var reject = command.GetCommand<FileTransferReject>();
                return HandleFileTransferRejectCommand(reject);
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"{_client.ServerMark} 处理文件传输响应异常", ex,
                $"{_client.ServerMark} 处理文件传输响应异常，详细信息请查看日志文件");
            return true;
        }
    }


    /// <summary>
    ///     处理服务端下载请求（内部方法）- 服务端请求客户端接收文件
    /// </summary>
    private async Task HandleFileDownloadRequestAsync(FileDownloadRequest request)
    {
        var localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            request.FileName);
        var tempFilePath = GetPartFilePath(localPath);
        if (!File.Exists(tempFilePath) && File.Exists(localPath))
        {
            File.Copy(localPath, tempFilePath, true);
        }

        var alreadyTransferredBytes = File.Exists(tempFilePath)
            ? new FileInfo(tempFilePath).Length
            : request.AlreadyTransferredBytes;

        var context = new DownloadContext
        {
            TaskId = request.TaskId,
            FileName = request.FileName,
            FileSize = request.FileSize,
            FileHash = request.FileHash,
            LocalFilePath = localPath,
            RemoteFilePath = request.RemoteFilePath,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            CancellationToken = CancellationToken.None
        };
        context.TempFilePath = tempFilePath;
        _downloadContexts[ClientTransferContextKeys.GetTransferKey(request.RemoteFilePath, request.TaskId)] = context;

        var response = new FileDownloadResponse
        {
            TaskId = request.TaskId,
            Accept = true,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            RemoteFilePath = request.RemoteFilePath
        };
        await _client.SendCommandAsync(response);
        Logger.Info(
            $"{_client.ServerMark} 收到服务端下载请求：{request.FileName}，已传输：{request.AlreadyTransferredBytes}字节，等待服务端发送文件块...");
    }

    /// <summary>
    ///     处理文件上传响应命令（内部方法）
    /// </summary>
    private async Task HandleFileUploadResponseAsync(FileUploadResponse response)
    {
        if (_uploadContexts.TryGetValue(
                ClientTransferContextKeys.GetTransferKey(response.RemoteFilePath, response.TaskId), out var context))
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                await CancelUploadAsync(context, "客户端已取消上传");
                return;
            }

            if (!response.Accept)
            {
                _uploadContexts.TryRemove(
                    ClientTransferContextKeys.GetTransferKey(response.RemoteFilePath, response.TaskId), out _);
                FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                    response.TaskId, context.FileName, context.RemoteFilePath, true, false, false,
                    response.Message));
                return;
            }

            if (response.AlreadyTransferredBytes < 0 ||
                response.AlreadyTransferredBytes > context.FileSize)
            {
                await CancelUploadAsync(context, "服务端返回的上传偏移无效");
                return;
            }

            context.AlreadyTransferredBytes = response.AlreadyTransferredBytes;
            Logger.Info($"{_client.ServerMark} 收到服务端上传响应：已传输{response.AlreadyTransferredBytes}字节，开始发送文件块...");
            if (context.FileSize == 0 || context.AlreadyTransferredBytes == context.FileSize)
            {
                _uploadContexts.TryRemove(
                    ClientTransferContextKeys.GetTransferKey(response.RemoteFilePath, response.TaskId), out _);
                TryDeleteTransferProgress(context.LocalFilePath);
                FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                    context.TaskId, context.FileName, context.RemoteFilePath, true, true, false,
                    "上传完成"));
                return;
            }

            await SendUploadBlockAsync(context.LocalFilePath, context.RemoteFilePath, context.FileSize,
                context.FileName, context.AlreadyTransferredBytes, context.TaskId, context.CancellationToken);
        }
        else
        {
            Logger.Warn($"{_client.ServerMark} 收到服务端上传响应但无上传会话，忽略");
        }
    }

    /// <summary>
    ///     处理文件下载响应命令（内部方法）- 服务端确认下载，开始发送文件块
    /// </summary>
    private async Task HandleFileDownloadResponseAsync(FileDownloadResponse response)
    {
        if (_downloadContexts.TryGetValue(
                ClientTransferContextKeys.GetTransferKey(response.RemoteFilePath, response.TaskId), out var context))
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                await CancelDownloadAsync(context, "客户端已取消下载");
                return;
            }

            if (response.FileSize < 0 || response.AlreadyTransferredBytes < 0 ||
                response.AlreadyTransferredBytes > response.FileSize)
            {
                await CancelDownloadAsync(context, "服务端返回的下载范围无效");
                return;
            }

            if (!response.Accept)
            {
                Logger.Warn($"{_client.ServerMark} 服务端拒绝下载请求：{response.Message}");
                _downloadContexts.TryRemove(
                    ClientTransferContextKeys.GetTransferKey(response.RemoteFilePath, response.TaskId), out _);
                FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                    response.TaskId,
                    context.FileName,
                    context.RemoteFilePath,
                    false,
                    false,
                    false,
                    response.Message));
                return;
            }

            context.AlreadyTransferredBytes = response.AlreadyTransferredBytes;
            context.FileSize = response.FileSize > 0 ? response.FileSize : context.FileSize;
            context.FileHash = response.FileHash;
            Logger.Info(
                $"{_client.ServerMark} 收到服务端下载响应：文件大小{context.FileSize}字节，已传输{response.AlreadyTransferredBytes}字节，等待接收文件块...");

            if (context.FileSize == 0 || context.AlreadyTransferredBytes == context.FileSize)
            {
                await CompleteDownloadAsync(context);
            }
        }
        else
        {
            Logger.Warn($"{_client.ServerMark} 收到服务端下载响应但无下载会话，忽略");
        }
    }

    /// <summary>
    ///     处理文件分块确认命令（内部方法）
    /// </summary>
    private async Task HandleFileChunkAckAsync(FileChunkAck chunkAck)
    {
        if (chunkAck.Success &&
            _uploadContexts.TryGetValue(
                ClientTransferContextKeys.GetTransferKey(chunkAck.RemoteFilePath, chunkAck.TaskId), out var context))
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                await CancelUploadAsync(context, "客户端已取消上传");
                return;
            }

            var nextOffset = chunkAck.AlreadyTransferredBytes;
            context.AlreadyTransferredBytes = nextOffset;
            if (nextOffset >= context.FileSize)
            {
                _uploadContexts.TryRemove(
                    ClientTransferContextKeys.GetTransferKey(chunkAck.RemoteFilePath, chunkAck.TaskId), out _);
                TryDeleteTransferProgress(context.LocalFilePath);
                Logger.Info($"{_client.ServerMark} 文件上传完成：{context.LocalFilePath} -> {context.RemoteFilePath}");
                FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                    context.TaskId,
                    context.FileName,
                    context.RemoteFilePath,
                    true,
                    true,
                    false,
                    "上传完成"));
                return;
            }

            await SendUploadBlockAsync(context.LocalFilePath, context.RemoteFilePath, context.FileSize,
                context.FileName, nextOffset, context.TaskId, context.CancellationToken);
        }
        else if (!chunkAck.Success)
        {
            Logger.Error($"{_client.ServerMark} 服务器报告文件块传输失败：{chunkAck.Message}");
            if (_uploadContexts.TryRemove(
                    ClientTransferContextKeys.GetTransferKey(chunkAck.RemoteFilePath, chunkAck.TaskId),
                    out var failedContext))
            {
                FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                    chunkAck.TaskId,
                    failedContext.FileName,
                    failedContext.RemoteFilePath,
                    true,
                    false,
                    false,
                    chunkAck.Message));
            }
        }
    }

    /// <summary>
    ///     处理文件传输拒绝命令（内部方法）
    /// </summary>
    private bool HandleFileTransferRejectCommand(FileTransferReject reject)
    {
        var errorMessage = reject.ErrorCode switch
        {
            FileTransferErrorCode.UploadServerFileLarger => "服务端文件大于客户端已有文件",
            FileTransferErrorCode.UploadFileAlreadyExists => "文件已存在，无需重复上传",
            FileTransferErrorCode.UploadFileHashMismatch => "文件大小相同但Hash不同，拒绝上传",
            FileTransferErrorCode.DownloadServerFileNotFound => "服务端文件不存在",
            FileTransferErrorCode.DownloadServerFileSmaller => "服务端文件小于客户端已有文件",
            FileTransferErrorCode.DownloadFileIdentical => "文件相同，不需要下载",
            FileTransferErrorCode.DownloadFileHashMismatch => "文件大小相同但Hash不同",
            FileTransferErrorCode.InvalidTransferRequest => "传输请求参数无效",
            _ => $"未知错误：{reject.ErrorCode}，{reject.Message}"
        };

        Logger.Warn($"{_client.ServerMark} 文件传输被拒绝：{reject.FileName}，错误码：{reject.ErrorCode}，原因：{errorMessage}");

        if (_uploadContexts.TryGetValue(ClientTransferContextKeys.GetTransferKey(reject.RemoteFilePath, reject.TaskId),
                out var uploadContext))
        {
            FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                reject.TaskId,
                reject.FileName,
                reject.RemoteFilePath,
                uploadContext.AlreadyTransferredBytes,
                uploadContext.FileSize,
                uploadContext.FileSize > 0
                    ? (double)uploadContext.AlreadyTransferredBytes / uploadContext.FileSize * 100
                    : 0,
                true));
            _uploadContexts.TryRemove(ClientTransferContextKeys.GetTransferKey(reject.RemoteFilePath, reject.TaskId),
                out _);
            FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                reject.TaskId,
                reject.FileName,
                reject.RemoteFilePath,
                true,
                reject.ErrorCode == FileTransferErrorCode.UploadFileAlreadyExists,
                false,
                errorMessage));
            return true;
        }

        if (_downloadContexts.TryGetValue(
                ClientTransferContextKeys.GetTransferKey(reject.RemoteFilePath, reject.TaskId),
                out var downloadContext))
        {
            FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                reject.TaskId,
                reject.FileName,
                reject.RemoteFilePath,
                downloadContext.AlreadyTransferredBytes,
                downloadContext.FileSize,
                downloadContext.FileSize > 0
                    ? (double)downloadContext.AlreadyTransferredBytes / downloadContext.FileSize * 100
                    : 0,
                false));
            _downloadContexts.TryRemove(ClientTransferContextKeys.GetTransferKey(reject.RemoteFilePath, reject.TaskId),
                out _);
            TryDeleteTransferProgress(downloadContext.TempFilePath);
            if (reject.ErrorCode != FileTransferErrorCode.DownloadFileIdentical)
            {
                TryDeleteFile(downloadContext.TempFilePath);
            }
            FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                reject.TaskId,
                reject.FileName,
                reject.RemoteFilePath,
                false,
                false,
                false,
                errorMessage));
            return true;
        }

        return false;
    }

    /// <summary>
    ///     处理接收到的文件块数据（用于下载：客户端接收服务器发送的文件数据）
    /// </summary>
    /// <param name="chunkData">文件分块数据</param>
    public async Task HandleFileChunkDataAsync(FileChunkData chunkData)
    {
        var remoteFilePath = chunkData.RemoteFilePath;
        if (string.IsNullOrEmpty(remoteFilePath))
        {
            Logger.Error($"{_client.ServerMark} 文件块数据缺少RemoteFilePath");
            return;
        }

        if (!_downloadContexts.TryGetValue(ClientTransferContextKeys.GetTransferKey(remoteFilePath, chunkData.TaskId),
                out var context))
        {
            Logger.Warn($"{_client.ServerMark} 收到文件块但未找到下载会话：{remoteFilePath}");
            return;
        }

        try
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                await CancelDownloadAsync(context, "客户端已取消下载");
                return;
            }

            if (!IsValidChunk(context, chunkData, out var chunkError))
            {
                await SendChunkAckAsync(context, chunkData, false, chunkError, context.AlreadyTransferredBytes);
                _downloadContexts.TryRemove(
                    ClientTransferContextKeys.GetTransferKey(remoteFilePath, chunkData.TaskId), out _);
                return;
            }

            var localFilePath = context.TempFilePath;
            var directory = Path.GetDirectoryName(localFilePath) ?? ".";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            long totalBytes;
            await using (var fs = new FileStream(localFilePath, FileMode.OpenOrCreate, FileAccess.Write,
                         FileShare.Read))
            {
                if (fs.Length != context.AlreadyTransferredBytes)
                {
                    throw new InvalidDataException("本地临时文件长度与下载会话不一致");
                }

                fs.Position = chunkData.Offset;
                await fs.WriteAsync(chunkData.Data.AsMemory(), context.CancellationToken);
                await fs.FlushAsync(context.CancellationToken);
                totalBytes = chunkData.Offset + chunkData.BlockSize;
            }

            context.AlreadyTransferredBytes = totalBytes;

            await SaveTransferProgressAsync(localFilePath, totalBytes, context.CancellationToken);

            var progress = context.FileSize > 0 ? (double)totalBytes / context.FileSize * 100 : 0;
            FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                context.TaskId,
                context.FileName,
                context.RemoteFilePath,
                totalBytes,
                context.FileSize,
                progress,
                false));

            if (totalBytes == context.FileSize)
            {
                var localHash = await ComputeFileHashAsync(localFilePath, context.CancellationToken);
                if (!string.IsNullOrWhiteSpace(context.FileHash) &&
                    !string.Equals(localHash, context.FileHash, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error($"{_client.ServerMark} 文件下载完成但哈希校验失败：{localFilePath}");
                    TryDeleteFile(localFilePath);
                    TryDeleteTransferProgress(localFilePath);
                    _downloadContexts.TryRemove(
                        ClientTransferContextKeys.GetTransferKey(remoteFilePath, chunkData.TaskId), out _);
                    await SendChunkAckAsync(context, chunkData, false, "下载完成，但哈希校验失败", totalBytes);
                    FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                        context.TaskId, context.FileName, context.RemoteFilePath, false, false, false,
                        "下载完成，但哈希校验失败"));
                    return;
                }

                await SendChunkAckAsync(context, chunkData, true, string.Empty, totalBytes);
                await CompleteDownloadAsync(context);
                return;
            }

            await SendChunkAckAsync(context, chunkData, true, string.Empty, totalBytes);
        }
        catch (Exception ex)
        {
            Logger.Error($"{_client.ServerMark} 处理文件块({chunkData.BlockIndex})异常", ex);
            await SendChunkAckAsync(context, chunkData, false, ex.Message, context.AlreadyTransferredBytes);
        }
    }


    /// <summary>
    ///     发送上传文件块（内部方法，用于上传时发送文件数据到服务器）
    /// </summary>
    /// <param name="localFilePath">本地文件路径</param>
    /// <param name="remoteFilePath">远程文件路径</param>
    /// <param name="fileSize">文件大小</param>
    /// <param name="fileName">文件名</param>
    /// <param name="alreadyTransferredBytes">已传输字节数</param>
    internal async Task SendUploadBlockAsync(string localFilePath, string remoteFilePath, long fileSize,
        string fileName, long alreadyTransferredBytes, int taskId,
        CancellationToken cancellationToken = default)
    {
        if (fileSize < 0 || alreadyTransferredBytes < 0 || alreadyTransferredBytes > fileSize)
        {
            throw new InvalidDataException("上传文件范围无效");
        }

        if (!File.Exists(localFilePath))
        {
            Logger.Error($"{_client.ServerMark} 文件不存在：{localFilePath}");
            return;
        }

        await using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Position = alreadyTransferredBytes;

        var blockSize = (int)Math.Min(FileTransferBlockSize, fileSize - alreadyTransferredBytes);
        if (blockSize <= 0)
        {
            return;
        }

        var buffer = new byte[blockSize];
        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, blockSize), cancellationToken);

        if (bytesRead == 0)
        {
            return;
        }

        var blockIndex = alreadyTransferredBytes / FileTransferBlockSize;
        var chunkData = new FileChunkData
        {
            TaskId = taskId,
            BlockIndex = blockIndex,
            Offset = alreadyTransferredBytes,
            BlockSize = bytesRead,
            Data = bytesRead == blockSize ? buffer : buffer.AsSpan(0, bytesRead).ToArray(),
            RemoteFilePath = remoteFilePath
        };

        await _client.SendCommandAsync(chunkData, cancellationToken);
        Logger.Info($"{_client.ServerMark} 发送文件块({blockIndex})：{bytesRead}字节");

        var newTransferredBytes = alreadyTransferredBytes + bytesRead;
        var progress = fileSize > 0 ? (double)newTransferredBytes / fileSize * 100 : 0;
        FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
            taskId,
            fileName,
            remoteFilePath,
            newTransferredBytes,
            fileSize,
            progress,
            true));
    }

    private async Task SendChunkAckAsync(DownloadContext context, FileChunkData chunkData, bool success,
        string message, long alreadyTransferredBytes)
    {
        await _client.SendCommandAsync(new FileChunkAck
        {
            TaskId = context.TaskId,
            BlockIndex = chunkData.BlockIndex,
            Success = success,
            Message = message,
            RemoteFilePath = context.RemoteFilePath,
            AlreadyTransferredBytes = alreadyTransferredBytes
        }, CancellationToken.None);
    }

    private async Task CompleteDownloadAsync(DownloadContext context)
    {
        var tempFilePath = context.TempFilePath;
        if (!File.Exists(tempFilePath))
        {
            await using var emptyFile = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write,
                FileShare.Read);
        }

        if (!string.IsNullOrWhiteSpace(context.FileHash))
        {
            var localHash = await ComputeFileHashAsync(tempFilePath, context.CancellationToken);
            if (!string.Equals(localHash, context.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(tempFilePath);
                TryDeleteTransferProgress(tempFilePath);
                _downloadContexts.TryRemove(
                    ClientTransferContextKeys.GetTransferKey(context.RemoteFilePath, context.TaskId), out _);
                FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
                    context.TaskId, context.FileName, context.RemoteFilePath, false, false, false,
                    "下载完成，但哈希校验失败"));
                return;
            }
        }

        File.Move(tempFilePath, context.LocalFilePath, true);
        TryDeleteTransferProgress(tempFilePath);
        _downloadContexts.TryRemove(ClientTransferContextKeys.GetTransferKey(context.RemoteFilePath, context.TaskId),
            out _);
        Logger.Info($"{_client.ServerMark} 文件下载完成：{context.RemoteFilePath} -> {context.LocalFilePath}");
        FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
            context.TaskId, context.FileName, context.RemoteFilePath, false, true, false, "下载完成"));
    }

    private static bool IsValidChunk(DownloadContext context, FileChunkData chunkData,
        [NotNullWhen(false)] out string? errorMessage)
    {
        if (chunkData.Data == null || chunkData.Data.Length == 0)
        {
            errorMessage = "文件块不能为空";
            return false;
        }

        if (chunkData.BlockSize != chunkData.Data.Length ||
            chunkData.BlockSize > TcpSocketClientFileSystemFeature.FileTransferBlockSize)
        {
            errorMessage = "文件块大小与数据长度不一致";
            return false;
        }

        if (chunkData.Offset != context.AlreadyTransferredBytes || chunkData.Offset < 0)
        {
            errorMessage = "文件块偏移不是当前下载位置";
            return false;
        }

        if (chunkData.BlockIndex != chunkData.Offset / TcpSocketClientFileSystemFeature.FileTransferBlockSize)
        {
            errorMessage = "文件块序号与偏移不一致";
            return false;
        }

        if (chunkData.Offset > context.FileSize - chunkData.BlockSize)
        {
            errorMessage = "文件块超出文件大小";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private async Task CancelUploadAsync(UploadContext context, string message)
    {
        _uploadContexts.TryRemove(ClientTransferContextKeys.GetTransferKey(context.RemoteFilePath, context.TaskId),
            out _);
        await NotifyTransferCancelledAsync(context.TaskId, context.FileName, context.RemoteFilePath, true, message);
    }

    private async Task CancelDownloadAsync(DownloadContext context, string message)
    {
        _downloadContexts.TryRemove(ClientTransferContextKeys.GetTransferKey(context.RemoteFilePath, context.TaskId),
            out _);
        await NotifyTransferCancelledAsync(context.TaskId, context.FileName, context.RemoteFilePath, false, message);
    }

    private async Task NotifyTransferCancelledAsync(int taskId, string fileName, string remoteFilePath, bool isUpload,
        string message)
    {
        try
        {
            await _client.SendCommandAsync(new FileTransferReject
            {
                TaskId = taskId,
                FileName = fileName,
                RemoteFilePath = remoteFilePath,
                ErrorCode = FileTransferErrorCode.UnknownError,
                Message = message
            });
        }
        catch (Exception ex)
        {
            Logger.Warn($"{_client.ServerMark} 发送取消传输通知失败：{remoteFilePath}，{ex.Message}");
        }

        FileTransferOutcome?.Invoke(this, new FileTransferOutcomeEventArgs(
            taskId,
            fileName,
            remoteFilePath,
            isUpload,
            false,
            true,
            message));
    }

    private static async Task<string> ComputeFileHashAsync(string filePath,
        CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(fs, cancellationToken);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    ///     获取已传输的字节数（用于断点续传）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>已传输的字节数</returns>
    private static long GetExistingTransferBytes(string filePath)
    {
        var progressFilePath = filePath + ".progress";
        if (File.Exists(progressFilePath))
        {
            var content = File.ReadAllText(progressFilePath);
            if (long.TryParse(content, out var bytes))
            {
                return bytes;
            }
        }

        return 0;
    }

    /// <summary>
    ///     保存传输进度到本地文件（用于断点续传）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="bytes">已传输的字节数</param>
    private static async Task SaveTransferProgressAsync(string filePath, long bytes,
        CancellationToken cancellationToken = default)
    {
        var progressFilePath = filePath + ".progress";
        await File.WriteAllTextAsync(progressFilePath, bytes.ToString(), cancellationToken);
    }

    private static void TryDeleteTransferProgress(string filePath)
    {
        var progressFilePath = filePath + ".progress";
        if (File.Exists(progressFilePath))
        {
            File.Delete(progressFilePath);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static string GetPartFilePath(string filePath) => filePath + ".part";
}

internal class UploadContext
{
    public int TaskId { get; set; }
    public string LocalFilePath { get; set; } = string.Empty;
    public string RemoteFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public long AlreadyTransferredBytes { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

internal class DownloadContext
{
    public int TaskId { get; set; }
    public string LocalFilePath { get; set; } = string.Empty;
    public string TempFilePath { get; set; } = string.Empty;
    public string RemoteFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public long AlreadyTransferredBytes { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

internal static class ClientTransferContextKeys
{
    public static string GetTransferKey(string remoteFilePath, int taskId) => $"{taskId}|{remoteFilePath}";
}
