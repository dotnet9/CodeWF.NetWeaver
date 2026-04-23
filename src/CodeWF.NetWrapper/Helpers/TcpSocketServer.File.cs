using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Models;
using CodeWF.NetWrapper.Requests;
using CodeWF.NetWrapper.Response;
using System;
using System.Collections.Generic;
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
                if (command.IsCommand<QueryFileStart>())
                {
                    var queryInfo = command.GetCommand<QueryFileStart>();
                    await HandleQueryFileStartAsync(command.Client!, queryInfo);
                }
                else if (command.IsCommand<CreateDirectoryStart>())
                {
                    var createInfo = command.GetCommand<CreateDirectoryStart>();
                    await HandleCreateDirectoryStartAsync(command.Client!, createInfo);
                }
                else if (command.IsCommand<DeleteFileStart>())
                {
                    var deleteInfo = command.GetCommand<DeleteFileStart>();
                    await HandleDeleteFileStartAsync(command.Client!, deleteInfo);
                }
                else if (command.IsCommand<FileUploadStart>())
                {
                    var startInfo = command.GetCommand<FileUploadStart>();
                    await HandleFileUploadStartAsync(command.Client!, startInfo);
                }
                else if (command.IsCommand<FileBlockData>())
                {
                    var blockData = command.GetCommand<FileBlockData>();
                    await HandleFileBlockDataAsync(command.Client!, blockData);
                }
                else if (command.IsCommand<FileDownloadStart>())
                {
                    var startInfo = command.GetCommand<FileDownloadStart>();
                    await HandleFileDownloadStartAsync(command.Client!, startInfo);
                }
                else if (command.IsCommand<FileBlockAck>())
                {
                    var blockAck = command.GetCommand<FileBlockAck>();
                    await HandleFileBlockAckAsync(command.Client!, blockAck);
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
    private async Task HandleQueryFileStartAsync(Socket client, QueryFileStart queryInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = queryInfo.TaskId;
        var directoryPath = queryInfo.DirectoryPath;

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            await SendDiskInfoListAsync(client, taskId, clientKey);
            return;
        }

        if (!Directory.Exists(directoryPath))
        {
            await SendDirectoryNotFoundErrorAsync(client, taskId, directoryPath, clientKey);
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

        var response = new DiskInfoListResponse
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
            var totalPages = (int)Math.Ceiling(sortedEntries.Count / (double)pageSize);

            for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                var pageEntries = sortedEntries.Skip(pageIndex * pageSize).Take(pageSize).ToList();
                var response = new DirectoryEntryResponse
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
        var info = new FileInfo(entryPath);
        var attributes = info.Attributes;
        var entryType = FileType.Unknown;

        if (attributes.HasFlag(FileAttributes.Directory))
        {
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
            Size = info.Exists ? info.Length : 0,
            LastModifiedTime = info.Exists ? info.LastWriteTime.Ticks : DateTime.Now.Ticks,
            EntryType = entryType
        };
    }


    /// <summary>
    /// 处理创建目录请求
    /// </summary>
    private async Task HandleCreateDirectoryStartAsync(Socket client, CreateDirectoryStart createInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = createInfo.TaskId;
        var directoryPath = createInfo.DirectoryPath;

        if (Directory.Exists(directoryPath))
        {
            var ack = new CreateDirectoryStartAck
            {
                TaskId = taskId,
                Success = true,
                DirectoryPath = directoryPath,
                Message = "目录已存在"
            };
            await SendCommandAsync(client, ack);
            Logger.Info($"{ServerMark} 客户端({clientKey})创建目录已存在：{directoryPath}");
            return;
        }

        try
        {
            Directory.CreateDirectory(directoryPath);
            var ack = new CreateDirectoryStartAck
            {
                TaskId = taskId,
                Success = true,
                DirectoryPath = directoryPath,
                Message = "创建成功"
            };
            await SendCommandAsync(client, ack);
            Logger.Info($"{ServerMark} 客户端({clientKey})创建目录成功：{directoryPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})创建目录失败：{directoryPath}", ex);
            var ack = new CreateDirectoryStartAck
            {
                TaskId = taskId,
                Success = false,
                DirectoryPath = directoryPath,
                Message = ex.Message
            };
            await SendCommandAsync(client, ack);
        }
    }

    /// <summary>
    /// 处理删除文件或目录请求
    /// </summary>
    private async Task HandleDeleteFileStartAsync(Socket client, DeleteFileStart deleteInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = deleteInfo.TaskId;
        var filePath = deleteInfo.FilePath;

        if (deleteInfo.IsDirectory)
        {
            if (!Directory.Exists(filePath))
            {
                var ack = new DeleteFileStartAck
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = filePath,
                    Message = "目录不存在"
                };
                await SendCommandAsync(client, ack);
                Logger.Warn($"{ServerMark} 客户端({clientKey})删除目录不存在：{filePath}");
                return;
            }

            try
            {
                Directory.Delete(filePath, false);
                var ack = new DeleteFileStartAck
                {
                    TaskId = taskId,
                    Success = true,
                    FilePath = filePath,
                    Message = "删除成功"
                };
                await SendCommandAsync(client, ack);
                Logger.Info($"{ServerMark} 客户端({clientKey})删除目录成功：{filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 客户端({clientKey})删除目录失败：{filePath}", ex);
                var ack = new DeleteFileStartAck
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = filePath,
                    Message = ex.Message
                };
                await SendCommandAsync(client, ack);
            }
        }
        else
        {
            if (!File.Exists(filePath))
            {
                var ack = new DeleteFileStartAck
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = filePath,
                    Message = "文件不存在"
                };
                await SendCommandAsync(client, ack);
                Logger.Warn($"{ServerMark} 客户端({clientKey})删除文件不存在：{filePath}");
                return;
            }

            try
            {
                File.Delete(filePath);
                var ack = new DeleteFileStartAck
                {
                    TaskId = taskId,
                    Success = true,
                    FilePath = filePath,
                    Message = "删除成功"
                };
                await SendCommandAsync(client, ack);
                Logger.Info($"{ServerMark} 客户端({clientKey})删除文件成功：{filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 客户端({clientKey})删除文件失败：{filePath}", ex);
                var ack = new DeleteFileStartAck
                {
                    TaskId = taskId,
                    Success = false,
                    FilePath = filePath,
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
    /// <param name="startInfo">文件上传开始信息</param>
    public async Task HandleFileUploadStartAsync(Socket client, FileUploadStart startInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = startInfo.TaskId;
        var remoteFilePath = startInfo.RemoteFilePath;
        var alreadyTransferredBytes = startInfo.AlreadyTransferredBytes;

        if (!File.Exists(remoteFilePath))
        {
            var uploadAck = new FileUploadStartAck
            {
                TaskId = taskId,
                Accept = true,
                AlreadyTransferredBytes = 0,
                RemoteFilePath = remoteFilePath,
                Message = "文件不存在，将创建新文件"
            };
            await SendCommandAsync(client, uploadAck);
            Logger.Info($"{ServerMark} 收到客户端({clientKey})上传请求：{startInfo.FileName} -> {remoteFilePath}，文件不存在，创建新文件");
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
                RemoteFilePath = remoteFilePath,
                FileName = startInfo.FileName
            };
            await SendCommandAsync(client, reject);
            Logger.Error($"{ServerMark} 收到客户端({clientKey})上传请求：{startInfo.FileName} -> {remoteFilePath}，服务端文件({actualTransferredBytes}字节)大于客户端已有文件({alreadyTransferredBytes}字节)");
            return;
        }

        if (actualTransferredBytes == alreadyTransferredBytes && actualTransferredBytes > 0)
        {
            if (startInfo.FileHash == ComputeFileHash(remoteFilePath))
            {
                var reject = new FileTransferReject
                {
                    TaskId = taskId,
                    ErrorCode = FileTransferErrorCode.UploadFileAlreadyExists,
                    Message = "文件已存在，无需重复上传",
                    RemoteFilePath = remoteFilePath,
                    FileName = startInfo.FileName
                };
                await SendCommandAsync(client, reject);
                Logger.Error($"{ServerMark} 收到客户端({clientKey})上传请求：{startInfo.FileName} -> {remoteFilePath}，文件已存在无需重复上传");
                return;
            }
            else
            {
                var reject = new FileTransferReject
                {
                    TaskId = taskId,
                    ErrorCode = FileTransferErrorCode.UploadFileHashMismatch,
                    Message = "文件大小相同但Hash不同",
                    RemoteFilePath = remoteFilePath,
                    FileName = startInfo.FileName
                };
                await SendCommandAsync(client, reject);
                Logger.Error($"{ServerMark} 收到客户端({clientKey})上传请求：{startInfo.FileName} -> {remoteFilePath}，文件大小相同但Hash不同");
                return;
            }
        }

        var ack = new FileUploadStartAck
        {
            TaskId = taskId,
            Accept = true,
            AlreadyTransferredBytes = actualTransferredBytes,
            RemoteFilePath = remoteFilePath,
            Message = "确认接收"
        };
        await SendCommandAsync(client, ack);
        Logger.Info(
            $"{ServerMark} 收到客户端({clientKey})上传请求：{startInfo.FileName} -> {remoteFilePath}，客户端报已传输：{alreadyTransferredBytes}字节，服务端确认：{actualTransferredBytes}字节，等待客户端发送文件块...");
    }


    /// <summary>
    /// 文件传输块大小（64KB），每次传输的数据块大小
    /// </summary>
    public const int FileTransferBlockSize = 64 * 1024;

    /// <summary>
    /// 处理接收到的文件块数据（用于下载：服务器接收客户端发送的文件数据）
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="blockData">文件块数据</param>
    public async Task HandleFileBlockDataAsync(Socket client, FileBlockData blockData)
    {
        var remoteFilePath = blockData.RemoteFilePath;
        if (string.IsNullOrEmpty(remoteFilePath))
        {
            Logger.Error($"{ServerMark} 文件块数据缺少RemoteFilePath");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(remoteFilePath) ?? ".";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fs = new FileStream(remoteFilePath, FileMode.Append, FileAccess.Write, FileShare.Write);
            await fs.WriteAsync(blockData.Data.AsMemory(0, blockData.BlockSize));

            var ack = new FileBlockAck
            {
                BlockIndex = blockData.BlockIndex,
                Success = true,
                RemoteFilePath = remoteFilePath,
                AlreadyTransferredBytes = fs.Length
            };
            await SendCommandAsync(client, ack);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 处理文件块({blockData.BlockIndex})异常", ex);
            var ack = new FileBlockAck
            {
                BlockIndex = blockData.BlockIndex,
                Success = false,
                Message = ex.Message,
                RemoteFilePath = remoteFilePath
            };
            await SendCommandAsync(client, ack);
        }
    }


    public async Task HandleFileDownloadStartAsync(Socket client, FileDownloadStart startInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var taskId = startInfo.TaskId;
        var remoteFilePath = startInfo.RemoteFilePath;
        var alreadyTransferredBytes = startInfo.AlreadyTransferredBytes;

        if (!File.Exists(remoteFilePath))
        {
            Logger.Error($"{ServerMark} 文件不存在：{remoteFilePath}");
            var reject = new FileTransferReject
            {
                TaskId = taskId,
                ErrorCode = FileTransferErrorCode.DownloadServerFileNotFound,
                Message = "服务端文件不存在",
                RemoteFilePath = remoteFilePath,
                FileName = startInfo.FileName
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
                RemoteFilePath = remoteFilePath,
                FileName = startInfo.FileName
            };
            await SendCommandAsync(client, reject);
            return;
        }

        if (totalFileSize == alreadyTransferredBytes)
        {
            var fileHash = ComputeFileHash(remoteFilePath);
            if (fileHash == startInfo.FileHash)
            {
                Logger.Error($"{ServerMark} 文件相同，不需要下载：{remoteFilePath}");
                var reject = new FileTransferReject
                {
                    TaskId = taskId,
                    ErrorCode = FileTransferErrorCode.DownloadFileIdentical,
                    Message = "文件相同，不需要下载",
                    RemoteFilePath = remoteFilePath,
                    FileName = startInfo.FileName
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
                    RemoteFilePath = remoteFilePath,
                    FileName = startInfo.FileName
                };
                await SendCommandAsync(client, reject);
                return;
            }
        }

        var ack = new FileDownloadStartAck
        {
            TaskId = taskId,
            Accept = true,
            FileSize = totalFileSize,
            FileHash = ComputeFileHash(remoteFilePath),
            AlreadyTransferredBytes = alreadyTransferredBytes,
            RemoteFilePath = remoteFilePath,
            Message = "确认传输"
        };
        await SendCommandAsync(client, ack);
        Logger.Info($"{ServerMark} 收到客户端({clientKey})下载请求：{remoteFilePath}，已传输：{alreadyTransferredBytes}字节，文件大小：{totalFileSize}字节，开始发送文件块...");
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
    /// 处理文件块传输应答
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="blockAck">文件块应答信息</param>
    public async Task HandleFileBlockAckAsync(Socket client, FileBlockAck blockAck)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        if (!blockAck.Success)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})报告文件块({blockAck.BlockIndex})传输失败：{blockAck.Message}");
        }
        else
        {
            Logger.Info($"{ServerMark} 客户端({clientKey})确认文件块({blockAck.BlockIndex})传输成功");
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
    public async Task SendFileBlockAsync(string clientKey, string localFilePath, long alreadyTransferredBytes,
        long fileSize, string fileName, string fileHash)
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
        var blockData = new FileBlockData
        {
            BlockIndex = blockIndex,
            Offset = alreadyTransferredBytes,
            BlockSize = bytesRead,
            Data = bytesRead == blockSize ? buffer : buffer.AsSpan(0, bytesRead).ToArray(),
            RemoteFilePath = localFilePath
        };

        await SendCommandAsync(session.TcpSocket, blockData);
        Logger.Info($"{ServerMark} 向客户端({clientKey})发送文件块({blockIndex})：{bytesRead}字节");
    }

    /// <summary>
    /// 发送文件传输完成命令（用于下载：服务器发送文件数据给客户端完成后）
    /// </summary>
    /// <param name="clientKey">客户端标识键</param>
    /// <param name="fileHash">文件哈希</param>
    public async Task SendFileTransferCompleteAsync(string clientKey, string fileHash)
    {
        if (!Clients.TryGetValue(clientKey, out var session) || session.TcpSocket == null)
        {
            return;
        }

        var completeCommand = new FileTransferComplete
        {
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
}