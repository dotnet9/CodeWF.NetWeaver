using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Models;
using CodeWF.NetWrapper.Requests;
using CodeWF.NetWrapper.Response;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CodeWF.NetWrapper.Helpers;

public partial class TcpSocketServer
{
    private readonly Channel<(string ClientKey, SocketCommand Command)> _fileTransferRequests =
        Channel.CreateUnbounded<(string, SocketCommand)>();
    private readonly ConcurrentDictionary<string, ServerUploadContext> _uploadContexts = new();
    private readonly ConcurrentDictionary<string, ServerDownloadContext> _downloadContexts = new();

    /// <summary>
    /// 服务端文件管理根目录。设置后，查询/创建/删除/上传/下载都会限制在该目录内。
    /// </summary>
    public string? FileSaveDirectory { get; set; }

    /// <summary>
    /// 服务端文件传输进度事件。
    /// </summary>
    public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgress;


    /// <summary>
    /// 处理文件传输请求（内部方法，在独立线程中运行）
    /// </summary>
    private async Task ProcessingFileTransferRequestsAsync()
    {
        await foreach (var (clientKey, command) in _fileTransferRequests.Reader.ReadAllAsync())
        {
            if (!Clients.TryGetValue(clientKey, out var client))
            {
                continue;
            }

            try
            {
                if (command.IsCommand<BrowseFileSystemRequest>())
                {
                    var queryInfo = command.GetCommand<BrowseFileSystemRequest>();
                    await HandleBrowseFileSystemAsync(command.Client!, queryInfo);
                }
                else if (command.IsCommand<CreateDirectoryRequest>())
                {
                    var createInfo = command.GetCommand<CreateDirectoryRequest>();
                    await HandleCreateDirectoryAsync(command.Client!, createInfo);
                }
                else if (command.IsCommand<DeletePathRequest>())
                {
                    var deleteInfo = command.GetCommand<DeletePathRequest>();
                    await HandleDeletePathAsync(command.Client!, deleteInfo);
                }
                else if (command.IsCommand<FileUploadRequest>())
                {
                    var request = command.GetCommand<FileUploadRequest>();
                    await HandleFileUploadRequestAsync(command.Client!, request);
                }
                else if (command.IsCommand<FileChunkData>())
                {
                    var chunkData = command.GetCommand<FileChunkData>();
                    await HandleFileChunkDataAsync(command.Client!, chunkData);
                }
                else if (command.IsCommand<FileDownloadRequest>())
                {
                    var request = command.GetCommand<FileDownloadRequest>();
                    await HandleFileDownloadRequestAsync(command.Client!, request);
                }
                else if (command.IsCommand<FileChunkAck>())
                {
                    var chunkAck = command.GetCommand<FileChunkAck>();
                    await HandleFileChunkAckAsync(command.Client!, chunkAck);
                }
                else if (command.IsCommand<FileTransferReject>())
                {
                    var reject = command.GetCommand<FileTransferReject>();
                    await HandleClientTransferRejectAsync(command.Client!, reject);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 处理文件传输请求异常", ex,
                    uiContent: $"{ServerMark} 处理文件传输请求异常，详细信息请查看日志文件");
            }
        }
    }


    /// <summary>
    /// 处理查询目录请求
    /// </summary>
    private async Task HandleBrowseFileSystemAsync(Socket client, BrowseFileSystemRequest queryInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = queryInfo.TaskId;
        var requestedDirectoryPath = queryInfo.DirectoryPath;

        if (string.IsNullOrWhiteSpace(FileSaveDirectory) && string.IsNullOrWhiteSpace(requestedDirectoryPath))
        {
            await SendDiskInfoListAsync(client, taskId, clientKey);
            return;
        }

        if (!TryResolveServerPath(requestedDirectoryPath, treatEmptyAsRoot: true, out var directoryPath,
                out var errorMessage))
        {
            await SendDirectoryAccessDeniedErrorAsync(client, taskId, requestedDirectoryPath, clientKey, errorMessage);
            return;
        }

        if (!Directory.Exists(directoryPath))
        {
            await SendDirectoryNotFoundErrorAsync(client, taskId, requestedDirectoryPath, clientKey);
            return;
        }

        await QueryAndSendDirectoryEntriesAsync(client, taskId, directoryPath, clientKey);
    }

    /// <summary>
    /// 发送磁盘信息列表
    /// </summary>
    private async Task SendDiskInfoListAsync(Socket client, int taskId, string clientKey)
    {
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        var diskInfos = new List<DiskInfo>();
        foreach (var drive in drives)
        {
            try
            {
                diskInfos.Add(new DiskInfo
                {
                    Name = drive.Name,
                    TotalSize = drive.TotalSize,
                    FreeSpace = drive.AvailableFreeSpace
                });
            }
            catch { }
        }

        var response = new DriveListResponse
        {
            TaskId = taskId,
            Disks = diskInfos
        };
        await SendCommandAsync(client, response);
        Logger.Info($"{ServerMark} 客户端({clientKey})查询磁盘信息完成，共{diskInfos.Count}个磁盘");
    }

    /// <summary>
    /// 发送目录不存在的错误
    /// </summary>
    private async Task SendDirectoryNotFoundErrorAsync(Socket client, int taskId, string directoryPath, string clientKey)
    {
        Logger.Error($"{ServerMark} 客户端({clientKey})查询目录不存在：{directoryPath}");
        var reject = new FileTransferReject
        {
            TaskId = taskId,
            ErrorCode = FileTransferErrorCode.DirectoryNotFound,
            Message = "目录不存在",
            RemoteFilePath = directoryPath
        };
        await SendCommandAsync(client, reject);
    }

    /// <summary>
    /// 发送目录访问被拒绝错误
    /// </summary>
    private async Task SendDirectoryAccessDeniedErrorAsync(Socket client, int taskId, string directoryPath,
        string clientKey, string message)
    {
        Logger.Error($"{ServerMark} 客户端({clientKey})查询目录被拒绝：{directoryPath}，原因：{message}");
        var reject = new FileTransferReject
        {
            TaskId = taskId,
            ErrorCode = FileTransferErrorCode.DirectoryAccessDenied,
            Message = message,
            RemoteFilePath = directoryPath
        };
        await SendCommandAsync(client, reject);
    }

    /// <summary>
    /// 查询目录条目并分页发送
    /// </summary>
    private async Task QueryAndSendDirectoryEntriesAsync(Socket client, int taskId, string directoryPath, string clientKey)
    {
        try
        {
            var entries = Directory.GetFileSystemEntries(directoryPath);
            var directoryEntries = new List<FileSystemEntry>();

            foreach (var entry in entries)
            {
                var fileSystemEntry = CreateFileSystemEntry(entry);
                directoryEntries.Add(fileSystemEntry);
            }

            var sortedEntries = directoryEntries
                .OrderByDescending(e => e.EntryType == FileType.Directory)
                .ThenBy(e => e.Name)
                .ToList();

            const int pageSize = 100;
            var totalPages = Math.Max(1, (int)Math.Ceiling(sortedEntries.Count / (double)pageSize));

            for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                var pageEntries = sortedEntries.Skip(pageIndex * pageSize).Take(pageSize).ToList();
                var response = new BrowseFileSystemResponse
                {
                    TaskId = taskId,
                    TotalCount = sortedEntries.Count,
                    PageSize = pageSize,
                    PageCount = totalPages,
                    PageIndex = pageIndex,
                    Entries = pageEntries
                };
                await SendCommandAsync(client, response);
            }

            Logger.Info($"{ServerMark} 客户端({clientKey})查询目录成功：{directoryPath}，共{directoryEntries.Count}个条目");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})查询目录无权限：{directoryPath}", ex);
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.DirectoryAccessDenied,
                Message = "无权限访问：" + ex.Message,
                RemoteFilePath = directoryPath
            };
            await SendCommandAsync(client, reject);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})查询目录异常：{directoryPath}", ex);
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.DirectoryAccessDenied,
                Message = ex.Message,
                RemoteFilePath = directoryPath
            };
            await SendCommandAsync(client, reject);
        }
    }

    /// <summary>
    /// 创建文件系统条目
    /// </summary>
    private static FileSystemEntry CreateFileSystemEntry(string entryPath)
    {
        // FileSystemInfo 是 FileInfo / DirectoryInfo 的公共父类，便于统一读取名称、时间、属性等元数据。
        FileSystemInfo info = Directory.Exists(entryPath)
            ? new DirectoryInfo(entryPath)
            : new FileInfo(entryPath);
        var attributes = info.Attributes;
        var entryType = FileType.Unknown;

        // HasFlag 用来判断某个枚举位是否存在，适合读取 FileAttributes 这种“按位组合”的标志枚举。
        if (attributes.HasFlag(FileAttributes.Directory))
        {
            // ReparsePoint 常见于快捷方式、联接点、符号链接等需要额外解析的目录项。
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entryType = FileType.Shortcut;
            }
            else
            {
                entryType = FileType.Directory;
            }
        }
        else if (info.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            entryType = FileType.Shortcut;
        }
        else
        {
            entryType = FileType.File;
        }

        return new FileSystemEntry
        {
            Name = info.Name,
            Size = info is FileInfo fileInfo && fileInfo.Exists ? fileInfo.Length : 0,
            LastModifiedTime = info.Exists ? info.LastWriteTime.Ticks : DateTime.Now.Ticks,
            EntryType = entryType
        };
    }


    /// <summary>
    /// 处理创建目录请求
    /// </summary>
    private async Task HandleCreateDirectoryAsync(Socket client, CreateDirectoryRequest createInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = createInfo.TaskId;
        var requestedDirectoryPath = createInfo.DirectoryPath;

        if (!TryResolveServerPath(requestedDirectoryPath, treatEmptyAsRoot: false, out var directoryPath,
                out var errorMessage))
        {
            var denyAck = new CreateDirectoryResponse
            {
                TaskId = taskId,
                Success = false,
                DirectoryPath = requestedDirectoryPath,
                Message = errorMessage
            };
            await SendCommandAsync(client, denyAck);
            return;
        }

        if (Directory.Exists(directoryPath))
        {
            var ack = new CreateDirectoryResponse
            {
                TaskId = taskId,
                Success = true,
                DirectoryPath = requestedDirectoryPath,
                Message = "目录已存在"
            };
            await SendCommandAsync(client, ack);
            Logger.Info($"{ServerMark} 客户端({clientKey})创建目录已存在：{directoryPath}");
            return;
        }

        try
        {
            Directory.CreateDirectory(directoryPath);
            var ack = new CreateDirectoryResponse
            {
                TaskId = taskId,
                Success = true,
                DirectoryPath = requestedDirectoryPath,
                Message = "创建成功"
            };
            await SendCommandAsync(client, ack);
            Logger.Info($"{ServerMark} 客户端({clientKey})创建目录成功：{directoryPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})创建目录失败：{directoryPath}", ex);
            var ack = new CreateDirectoryResponse
            {
                TaskId = taskId,
                Success = false,
                DirectoryPath = requestedDirectoryPath,
                Message = ex.Message
            };
            await SendCommandAsync(client, ack);
        }
    }

    /// <summary>
    /// 处理删除文件或目录请求
    /// </summary>
    private async Task HandleDeletePathAsync(Socket client, DeletePathRequest deleteInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = deleteInfo.TaskId;
        var requestedFilePath = deleteInfo.FilePath;

        if (!TryResolveServerPath(requestedFilePath, treatEmptyAsRoot: false, out var filePath,
                out var errorMessage))
        {
            var denyAck = new DeletePathResponse
            {
                TaskId = taskId,
                Success = false,
                FilePath = requestedFilePath,
                Message = errorMessage
            };
            await SendCommandAsync(client, denyAck);
            return;
        }

        if (deleteInfo.IsDirectory)
        {
            if (!Directory.Exists(filePath))
            {
                var ack = new DeletePathResponse
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = requestedFilePath,
                    Message = "目录不存在"
                };
                await SendCommandAsync(client, ack);
                Logger.Warn($"{ServerMark} 客户端({clientKey})删除目录不存在：{filePath}");
                return;
            }

            try
            {
                Directory.Delete(filePath, false);
                var ack = new DeletePathResponse
                {
                    TaskId = taskId,
                    Success = true,
                    FilePath = requestedFilePath,
                    Message = "删除成功"
                };
                await SendCommandAsync(client, ack);
                Logger.Info($"{ServerMark} 客户端({clientKey})删除目录成功：{filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 客户端({clientKey})删除目录失败：{filePath}", ex);
                var ack = new DeletePathResponse
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = requestedFilePath,
                    Message = ex.Message
                };
                await SendCommandAsync(client, ack);
            }
        }
        else
        {
            if (!File.Exists(filePath))
            {
                var ack = new DeletePathResponse
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = requestedFilePath,
                    Message = "文件不存在"
                };
                await SendCommandAsync(client, ack);
                Logger.Warn($"{ServerMark} 客户端({clientKey})删除文件不存在：{filePath}");
                return;
            }

            try
            {
                File.Delete(filePath);
                var ack = new DeletePathResponse
                {
                    TaskId = taskId,
                    Success = true,
                    FilePath = requestedFilePath,
                    Message = "删除成功"
                };
                await SendCommandAsync(client, ack);
                Logger.Info($"{ServerMark} 客户端({clientKey})删除文件成功：{filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 客户端({clientKey})删除文件失败：{filePath}", ex);
                var ack = new DeletePathResponse
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = requestedFilePath,
                    Message = ex.Message
                };
                await SendCommandAsync(client, ack);
            }
        }
    }

    /// <summary>
    /// 处理客户端上传文件请求
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="request">文件上传请求</param>
    public async Task HandleFileUploadRequestAsync(Socket client, FileUploadRequest request)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = request.TaskId;
        var requestedRemoteFilePath = request.RemoteFilePath;
        var alreadyTransferredBytes = request.AlreadyTransferredBytes;
        if (!TryResolveServerPath(requestedRemoteFilePath, treatEmptyAsRoot: false, out var remoteFilePath,
                out var errorMessage))
        {
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.DirectoryAccessDenied,
                Message = errorMessage,
                RemoteFilePath = requestedRemoteFilePath,
                FileName = request.FileName
            };
            await SendCommandAsync(client, reject);
            return;
        }

        var contextKey = GetTransferKey(clientKey, requestedRemoteFilePath, taskId);

        if (!File.Exists(remoteFilePath))
        {
            _uploadContexts[contextKey] = new ServerUploadContext
            {
                TaskId = taskId,
                ClientKey = clientKey,
                RequestedRemoteFilePath = requestedRemoteFilePath,
                ActualFilePath = remoteFilePath,
                FileName = request.FileName,
                FileSize = request.FileSize,
                FileHash = request.FileHash,
                AlreadyTransferredBytes = 0
            };
            var uploadResponse = new FileUploadResponse
            {
                TaskId = taskId,
                Accept = true,
                AlreadyTransferredBytes = 0,
                RemoteFilePath = requestedRemoteFilePath,
                Message = "文件不存在，将创建新文件"
            };
            await SendCommandAsync(client, uploadResponse);
            Logger.Info($"{ServerMark} 收到客户端({clientKey})上传请求：{request.FileName} -> {remoteFilePath}，文件不存在，创建新文件");
            return;
        }

        var fileInfo = new FileInfo(remoteFilePath);
        var actualTransferredBytes = fileInfo.Length;

        if (actualTransferredBytes > alreadyTransferredBytes)
        {
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.UploadServerFileLarger,
                Message = "服务端文件大于客户端已有文件",
                RemoteFilePath = requestedRemoteFilePath,
                FileName = request.FileName
            };
            await SendCommandAsync(client, reject);
            Logger.Error($"{ServerMark} 收到客户端({clientKey})上传请求：{request.FileName} -> {remoteFilePath}，服务端文件({actualTransferredBytes}字节)大于客户端已有文件({alreadyTransferredBytes}字节)");
            return;
        }

        if (actualTransferredBytes == alreadyTransferredBytes && actualTransferredBytes > 0)
        {
            if (request.FileHash == ComputeFileHash(remoteFilePath))
            {
                var reject = new FileTransferReject
                {
                    TaskId = taskId,
                    ErrorCode = FileTransferErrorCode.UploadFileAlreadyExists,
                    Message = "文件已存在，无需重复上传",
                    RemoteFilePath = requestedRemoteFilePath,
                    FileName = request.FileName
                };
                await SendCommandAsync(client, reject);
                Logger.Error($"{ServerMark} 收到客户端({clientKey})上传请求：{request.FileName} -> {remoteFilePath}，文件已存在无需重复上传");
                return;
            }
            else
            {
                var reject = new FileTransferReject
                {
                    TaskId = taskId,
                    ErrorCode = FileTransferErrorCode.UploadFileHashMismatch,
                    Message = "文件大小相同但Hash不同",
                    RemoteFilePath = requestedRemoteFilePath,
                    FileName = request.FileName
                };
                await SendCommandAsync(client, reject);
                Logger.Error($"{ServerMark} 收到客户端({clientKey})上传请求：{request.FileName} -> {remoteFilePath}，文件大小相同但Hash不同");
                return;
            }
        }

        _uploadContexts[contextKey] = new ServerUploadContext
        {
            TaskId = taskId,
            ClientKey = clientKey,
            RequestedRemoteFilePath = requestedRemoteFilePath,
            ActualFilePath = remoteFilePath,
            FileName = request.FileName,
            FileSize = request.FileSize,
            FileHash = request.FileHash,
            AlreadyTransferredBytes = actualTransferredBytes
        };
        var response = new FileUploadResponse
        {
            TaskId = taskId,
            Accept = true,
            AlreadyTransferredBytes = actualTransferredBytes,
            RemoteFilePath = requestedRemoteFilePath,
            Message = "确认接收"
        };
        await SendCommandAsync(client, response);
        Logger.Info(
            $"{ServerMark} 收到客户端({clientKey})上传请求：{request.FileName} -> {remoteFilePath}，客户端报已传输：{alreadyTransferredBytes}字节，服务端确认：{actualTransferredBytes}字节，等待客户端发送文件块...");
    }


    /// <summary>
    /// 文件传输块大小（64KB），每次传输的数据块大小
    /// </summary>
    public const int FileTransferBlockSize = 64 * 1024;

    /// <summary>
    /// 处理接收到的文件块数据（用于下载：服务器接收客户端发送的文件数据）
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="chunkData">文件分块数据</param>
    public async Task HandleFileChunkDataAsync(Socket client, FileChunkData chunkData)
    {
        var requestedRemoteFilePath = chunkData.RemoteFilePath;
        if (string.IsNullOrEmpty(requestedRemoteFilePath))
        {
            Logger.Error($"{ServerMark} 文件块数据缺少RemoteFilePath");
            return;
        }

        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var contextKey = GetTransferKey(clientKey, requestedRemoteFilePath, chunkData.TaskId);
        if (!_uploadContexts.TryGetValue(contextKey, out var context))
        {
            Logger.Error($"{ServerMark} 未找到上传会话：{clientKey} -> {requestedRemoteFilePath}");
            var missingAck = new FileChunkAck
            {
                TaskId = chunkData.TaskId,
                BlockIndex = chunkData.BlockIndex,
                Success = false,
                Message = "未找到上传会话",
                RemoteFilePath = requestedRemoteFilePath
            };
            await SendCommandAsync(client, missingAck);
            return;
        }

        try
        {
            var remoteFilePath = context.ActualFilePath;
            var directory = Path.GetDirectoryName(remoteFilePath) ?? ".";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // FileMode.OpenOrCreate: 支持首次上传时自动建文件，也支持断点续传时继续写入同一文件。
            await using var fs = new FileStream(remoteFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            // Position 指定实际写入偏移量，只有这样才能正确覆盖/续传指定区块，而不是无脑 Append。
            fs.Position = chunkData.Offset;
            await fs.WriteAsync(chunkData.Data.AsMemory(0, chunkData.BlockSize));
            await fs.FlushAsync();

            var totalBytes = Math.Max(fs.Length, chunkData.Offset + chunkData.BlockSize);
            context.AlreadyTransferredBytes = totalBytes;
            NotifyServerTransferProgress(context.FileName, totalBytes, context.FileSize, isUpload: true);

            var success = true;
            var message = string.Empty;
            if (context.FileSize > 0 && totalBytes >= context.FileSize)
            {
                var currentHash = await ComputeFileHashAsync(remoteFilePath);
                if (!string.Equals(currentHash, context.FileHash, StringComparison.OrdinalIgnoreCase))
                {
                    success = false;
                    message = "文件上传完成，但哈希校验失败";
                    Logger.Error($"{ServerMark} 客户端({clientKey})上传文件哈希校验失败：{remoteFilePath}");
                }
                else
                {
                    Logger.Info($"{ServerMark} 客户端({clientKey})上传文件完成：{remoteFilePath}");
                }

                _uploadContexts.TryRemove(contextKey, out _);
            }

            var ack = new FileChunkAck
            {
                TaskId = context.TaskId,
                BlockIndex = chunkData.BlockIndex,
                Success = success,
                Message = message,
                RemoteFilePath = requestedRemoteFilePath,
                AlreadyTransferredBytes = totalBytes
            };
            await SendCommandAsync(client, ack);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 处理文件块({chunkData.BlockIndex})异常", ex);
            var ack = new FileChunkAck
            {
                TaskId = context.TaskId,
                BlockIndex = chunkData.BlockIndex,
                Success = false,
                Message = ex.Message,
                RemoteFilePath = requestedRemoteFilePath
            };
            await SendCommandAsync(client, ack);
        }
    }


    public async Task HandleFileDownloadRequestAsync(Socket client, FileDownloadRequest request)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = request.TaskId;
        var requestedRemoteFilePath = request.RemoteFilePath;
        var alreadyTransferredBytes = request.AlreadyTransferredBytes;
        if (!TryResolveServerPath(requestedRemoteFilePath, treatEmptyAsRoot: false, out var remoteFilePath,
                out var errorMessage))
        {
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.DirectoryAccessDenied,
                Message = errorMessage,
                RemoteFilePath = requestedRemoteFilePath,
                FileName = request.FileName
            };
            await SendCommandAsync(client, reject);
            return;
        }

        if (!File.Exists(remoteFilePath))
        {
            Logger.Error($"{ServerMark} 文件不存在：{remoteFilePath}");
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.DownloadServerFileNotFound,
                Message = "服务端文件不存在",
                RemoteFilePath = requestedRemoteFilePath,
                FileName = request.FileName
            };
            await SendCommandAsync(client, reject);
            return;
        }

        var fileInfo = new FileInfo(remoteFilePath);
        var totalFileSize = fileInfo.Length;

        if (totalFileSize < alreadyTransferredBytes)
        {
            Logger.Error($"{ServerMark} 服务端文件小于客户端已有文件：{remoteFilePath}");
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.DownloadServerFileSmaller,
                Message = "服务端文件小于客户端已有文件",
                RemoteFilePath = requestedRemoteFilePath,
                FileName = request.FileName
            };
            await SendCommandAsync(client, reject);
            return;
        }

        if (totalFileSize == alreadyTransferredBytes)
        {
            var fileHash = ComputeFileHash(remoteFilePath);
            if (fileHash == request.FileHash)
            {
                Logger.Error($"{ServerMark} 文件相同，不需要下载：{remoteFilePath}");
                var reject = new FileTransferReject
                {
                    TaskId = taskId,
                    ErrorCode = FileTransferErrorCode.DownloadFileIdentical,
                    Message = "文件相同，不需要下载",
                    RemoteFilePath = requestedRemoteFilePath,
                    FileName = request.FileName
                };
                await SendCommandAsync(client, reject);
                return;
            }
            else
            {
                Logger.Error($"{ServerMark} 文件大小相同但Hash不同：{remoteFilePath}");
                var reject = new FileTransferReject
                {
                    TaskId = taskId,
                    ErrorCode = FileTransferErrorCode.DownloadFileHashMismatch,
                    Message = "文件大小相同但Hash不同",
                    RemoteFilePath = requestedRemoteFilePath,
                    FileName = request.FileName
                };
                await SendCommandAsync(client, reject);
                return;
            }
        }

        var resolvedFileHash = ComputeFileHash(remoteFilePath);
        _downloadContexts[GetTransferKey(clientKey, requestedRemoteFilePath, taskId)] = new ServerDownloadContext
        {
            TaskId = taskId,
            ClientKey = clientKey,
            RequestedRemoteFilePath = requestedRemoteFilePath,
            ActualFilePath = remoteFilePath,
            FileName = request.FileName,
            FileSize = totalFileSize,
            FileHash = resolvedFileHash,
            AlreadyTransferredBytes = alreadyTransferredBytes
        };

        var response = new FileDownloadResponse
        {
            TaskId = taskId,
            Accept = true,
            FileSize = totalFileSize,
            FileHash = resolvedFileHash,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            RemoteFilePath = requestedRemoteFilePath,
            Message = "确认传输"
        };
        await SendCommandAsync(client, response);
        Logger.Info($"{ServerMark} 收到客户端({clientKey})下载请求：{remoteFilePath}，已传输：{alreadyTransferredBytes}字节，文件大小：{totalFileSize}字节，开始发送文件块...");
        await SendFileBlockAsync(clientKey, remoteFilePath, requestedRemoteFilePath, alreadyTransferredBytes,
            totalFileSize, request.FileName, resolvedFileHash, taskId);
    }

    private static string ComputeFileHash(string filePath)
    {
        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = sha256.ComputeHash(fs);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 处理文件分块确认
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="chunkAck">文件分块确认信息</param>
    public async Task HandleFileChunkAckAsync(Socket client, FileChunkAck chunkAck)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        if (!chunkAck.Success)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})报告文件块({chunkAck.BlockIndex})传输失败：{chunkAck.Message}");
            _downloadContexts.TryRemove(GetTransferKey(clientKey, chunkAck.RemoteFilePath, chunkAck.TaskId), out _);
        }
        else
        {
            Logger.Info($"{ServerMark} 客户端({clientKey})确认文件块({chunkAck.BlockIndex})传输成功");
            var contextKey = GetTransferKey(clientKey, chunkAck.RemoteFilePath, chunkAck.TaskId);
            if (!_downloadContexts.TryGetValue(contextKey, out var context))
            {
                return;
            }

            context.AlreadyTransferredBytes = chunkAck.AlreadyTransferredBytes;
            NotifyServerTransferProgress(context.FileName, chunkAck.AlreadyTransferredBytes, context.FileSize,
                isUpload: false);
            if (chunkAck.AlreadyTransferredBytes >= context.FileSize)
            {
                _downloadContexts.TryRemove(contextKey, out _);
                Logger.Info($"{ServerMark} 向客户端({clientKey})发送文件完成：{context.ActualFilePath}");
                return;
            }

            await SendFileBlockAsync(clientKey, context.ActualFilePath, context.RequestedRemoteFilePath,
                chunkAck.AlreadyTransferredBytes, context.FileSize, context.FileName, context.FileHash,
                context.TaskId);
        }
    }

    /// <summary>
    /// 发送文件块到客户端（用于下载：服务器发送文件数据给客户端）
    /// </summary>
    /// <param name="clientKey">客户端标识键</param>
    /// <param name="localFilePath">本地文件路径</param>
    /// <param name="alreadyTransferredBytes">已传输字节数</param>
    /// <param name="fileSize">文件大小</param>
    /// <param name="fileName">文件名</param>
    /// <param name="fileHash">文件哈希</param>
    public async Task SendFileBlockAsync(string clientKey, string localFilePath, string remoteFilePath,
        long alreadyTransferredBytes, long fileSize, string fileName, string fileHash, int taskId)
    {
        if (!Clients.TryGetValue(clientKey, out var session) || session.TcpSocket == null)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})不存在或未连接");
            return;
        }

        if (!File.Exists(localFilePath))
        {
            Logger.Error($"{ServerMark} 文件不存在：{localFilePath}");
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
        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, blockSize));

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

        await SendCommandAsync(session.TcpSocket, chunkData);
        Logger.Info($"{ServerMark} 向客户端({clientKey})发送文件块({blockIndex})：{bytesRead}字节");
        var newTransferredBytes = alreadyTransferredBytes + bytesRead;
        NotifyServerTransferProgress(fileName, newTransferredBytes, fileSize, isUpload: false);
    }

    /// <summary>
    /// 发送文件传输完成命令（用于下载：服务器发送文件数据给客户端完成后）
    /// </summary>
    /// <param name="clientKey">客户端标识键</param>
    /// <param name="fileHash">文件哈希</param>
    public async Task SendFileTransferCompleteAsync(string clientKey, int taskId, string fileHash)
    {
        if (!Clients.TryGetValue(clientKey, out var session) || session.TcpSocket == null)
        {
            return;
        }

        var completeCommand = new FileTransferComplete
        {
            TaskId = taskId,
            FileHash = fileHash,
            Success = true
        };
        await SendCommandAsync(session.TcpSocket, completeCommand);
        Logger.Info($"{ServerMark} 向客户端({clientKey})发送文件传输完成命令");
    }

    /// <summary>
    /// 计算文件的SHA256哈希值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>十六进制哈希字符串</returns>
    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 获取已存在的传输进度（用于断点续传）
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileHash">文件哈希</param>
    /// <returns>已传输的字节数</returns>
    private long GetExistingTransferBytes(string fileName, string fileHash)
    {
        var progressFile = GetProgressFilePath(fileName, fileHash);
        if (File.Exists(progressFile))
        {
            var content = File.ReadAllText(progressFile);
            if (long.TryParse(content.Trim(), out var bytes))
            {
                return bytes;
            }
        }

        return 0;
    }

    /// <summary>
    /// 保存传输进度（用于断点续传）
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileHash">文件哈希</param>
    /// <param name="totalBytes">总字节数</param>
    private void SaveTransferProgress(string fileName, string fileHash, long totalBytes)
    {
        var progressFile = GetProgressFilePath(fileName, fileHash);
        File.WriteAllText(progressFile, totalBytes.ToString());
    }

    /// <summary>
    /// 获取传输进度文件路径
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileHash">文件哈希</param>
    /// <returns>进度文件路径</returns>
    private static string GetProgressFilePath(string fileName, string fileHash) =>
        Path.Combine(Path.GetTempPath(), $"file_transfer_server_{fileName}_{fileHash}.progress");

    private void NotifyServerTransferProgress(string fileName, long transferredBytes, long totalBytes, bool isUpload)
    {
        var progress = totalBytes > 0 ? (double)transferredBytes / totalBytes * 100 : 0;
        FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
            0,
            fileName,
            string.Empty,
            transferredBytes,
            totalBytes,
            progress,
            isUpload));
    }

    private Task HandleClientTransferRejectAsync(Socket client, FileTransferReject reject)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var uploadKey = GetTransferKey(clientKey, reject.RemoteFilePath, reject.TaskId);
        if (_uploadContexts.TryRemove(uploadKey, out _))
        {
            Logger.Warn($"{ServerMark} 客户端({clientKey})取消上传：{reject.RemoteFilePath}，原因：{reject.Message}");
            return Task.CompletedTask;
        }

        var downloadKey = GetTransferKey(clientKey, reject.RemoteFilePath, reject.TaskId);
        if (_downloadContexts.TryRemove(downloadKey, out _))
        {
            Logger.Warn($"{ServerMark} 客户端({clientKey})取消下载：{reject.RemoteFilePath}，原因：{reject.Message}");
        }

        return Task.CompletedTask;
    }

    private static string GetTransferKey(string clientKey, string remoteFilePath, int taskId) =>
        $"{clientKey}|{taskId}|{remoteFilePath}";

    private bool TryResolveServerPath(string requestedPath, bool treatEmptyAsRoot, out string resolvedPath,
        out string errorMessage)
    {
        resolvedPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(FileSaveDirectory))
        {
            if (treatEmptyAsRoot && string.IsNullOrWhiteSpace(requestedPath))
            {
                resolvedPath = string.Empty;
                return true;
            }

            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                errorMessage = "路径不能为空";
                return false;
            }

            resolvedPath = Path.GetFullPath(requestedPath);
            return true;
        }

        var rootPath = Path.GetFullPath(FileSaveDirectory);
        Directory.CreateDirectory(rootPath);

        if (treatEmptyAsRoot && (string.IsNullOrWhiteSpace(requestedPath) || requestedPath is "/" or "\\"))
        {
            resolvedPath = rootPath;
            return true;
        }

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            errorMessage = "路径不能为空";
            return false;
        }

        // Path.IsPathRooted 用来判断是否是绝对路径；若是相对路径，就把它拼到 FileSaveDirectory 下面。
        var candidatePath = Path.IsPathRooted(requestedPath)
            ? Path.GetFullPath(requestedPath)
            : Path.GetFullPath(Path.Combine(rootPath,
                requestedPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        // StartsWith(rootPath) 是最终的越权保护，确保规范化后的真实路径仍然落在允许根目录下。
        if (!candidatePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "路径超出服务端允许访问的根目录";
            return false;
        }

        resolvedPath = candidatePath;
        return true;
    }
}

internal sealed class ServerUploadContext
{
    public int TaskId { get; set; }
    public string ClientKey { get; set; } = string.Empty;
    public string RequestedRemoteFilePath { get; set; } = string.Empty;
    public string ActualFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public long AlreadyTransferredBytes { get; set; }
}

internal sealed class ServerDownloadContext
{
    public int TaskId { get; set; }
    public string ClientKey { get; set; } = string.Empty;
    public string RequestedRemoteFilePath { get; set; } = string.Empty;
    public string ActualFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public long AlreadyTransferredBytes { get; set; }
}
