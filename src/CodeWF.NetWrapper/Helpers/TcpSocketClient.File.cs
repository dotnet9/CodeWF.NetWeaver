using CodeWF.Log.Core;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Models;
using CodeWF.NetWrapper.Response;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeWF.NetWrapper.Requests;

namespace CodeWF.NetWrapper.Helpers;

public partial class TcpSocketClient
{
    private readonly Channel<SocketCommand> _fileTransferResponses = Channel.CreateUnbounded<SocketCommand>();

    /// <summary>
    /// 文件传输块大小（64KB），每次传输的数据块大小
    /// </summary>
    public const int FileTransferBlockSize = 64 * 1024;

    /// <summary>
    /// 文件传输进度事件，当文件传输进度更新时触发
    /// </summary>
    public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgress;

    private readonly ConcurrentDictionary<string, UploadContext> _uploadContexts = new();
    private readonly ConcurrentDictionary<string, DownloadContext> _downloadContexts = new();

    /// <summary>
    /// 查询服务端目录文件列表
    /// </summary>
    /// <param name="serverDirectoryPath">服务端目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task QueryServerDirectoryAsync(string serverDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法查询服务端目录");
            return;
        }

        var queryCommand = new QueryFileStart
        {
            DirectoryPath = serverDirectoryPath
        };

        await SendCommandAsync(queryCommand);
        Logger.Info($"{ServerMark} 请求查询服务端目录：{serverDirectoryPath}");
    }


    /// <summary>
    /// 在服务端创建目录
    /// </summary>
    /// <param name="serverDirectoryPath">服务端目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task CreateServerDirectoryAsync(string serverDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法创建服务端目录");
            return;
        }

        var createCommand = new CreateDirectoryStart
        {
            DirectoryPath = serverDirectoryPath
        };

        await SendCommandAsync(createCommand);
        Logger.Info($"{ServerMark} 请求创建服务端目录：{serverDirectoryPath}");
    }

    /// <summary>
    /// 删除服务端目录
    /// </summary>
    /// <param name="serverDirectoryPath">服务端目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task DeleteServerDirectoryAsync(string serverDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法删除服务端目录");
            return;
        }

        var deleteCommand = new DeleteFileStart
        {
            FilePath = serverDirectoryPath,
            IsDirectory = true
        };

        await SendCommandAsync(deleteCommand);
        Logger.Info($"{ServerMark} 请求删除服务端目录：{serverDirectoryPath}");
    }

    /// <summary>
    /// 删除服务端文件
    /// </summary>
    /// <param name="serverFilePath">服务端文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task DeleteServerFileAsync(string serverFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法删除服务端文件");
            return;
        }

        var deleteCommand = new DeleteFileStart
        {
            FilePath = serverFilePath,
            IsDirectory = false
        };

        await SendCommandAsync(deleteCommand);
        Logger.Info($"{ServerMark} 请求删除服务端文件：{serverFilePath}");
    }


    /// <summary>
    /// 开始向服务器上传文件
    /// </summary>
    /// <param name="localFilePath">本地上传文件路径</param>
    /// <param name="remoteFilePath">服务端保存文件路径（含文件名）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartFileUploadAsync(string localFilePath, string remoteFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法开始文件上传");
            return;
        }

        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            Logger.Error($"{ServerMark} 文件不存在：{localFilePath}");
            return;
        }

        var remoteFileName = Path.GetFileName(remoteFilePath);
        var fileHash = await ComputeFileHashAsync(localFilePath);

        var context = new UploadContext
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            LocalFilePath = localFilePath,
            RemoteFilePath = remoteFilePath,
            FileHash = fileHash,
            AlreadyTransferredBytes = 0
        };
        _uploadContexts[remoteFilePath] = context;

        var startCommand = new FileUploadStart
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            FileHash = fileHash,
            AlreadyTransferredBytes = 0,
            RemoteFilePath = remoteFilePath
        };

        await SendCommandAsync(startCommand);
        Logger.Info($"{ServerMark} 请求上传文件：{localFilePath} -> {remoteFilePath}，大小：{fileInfo.Length}字节，等待服务端响应...");
    }

    /// <summary>
    /// 请求从服务器下载文件
    /// </summary>
    /// <param name="serverFilePath">服务端待下载文件完整路径</param>
    /// <param name="localSaveDirectory">本地保存目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartFileDownloadAsync(string serverFilePath, string localSaveDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            Logger.Error($"{ServerMark} 未连接，无法开始文件下载");
            return;
        }

        var serverFileName = Path.GetFileName(serverFilePath);
        if (!Directory.Exists(localSaveDirectory))
        {
            Directory.CreateDirectory(localSaveDirectory);
        }

        var alreadyTransferredBytes = GetExistingTransferBytes(serverFilePath);
        var localSavePath = Path.Combine(localSaveDirectory, serverFileName);

        if (alreadyTransferredBytes == 0 && File.Exists(localSavePath))
        {
            alreadyTransferredBytes = new FileInfo(localSavePath).Length;
        }

        var context = new DownloadContext
        {
            FileName = serverFileName,
            FileSize = 0,
            FileHash = string.Empty,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            LocalFilePath = localSavePath,
            RemoteFilePath = serverFilePath
        };
        _downloadContexts[serverFilePath] = context;

        var startCommand = new FileDownloadStart
        {
            FileName = serverFileName,
            FileSize = 0,
            FileHash = string.Empty,
            AlreadyTransferredBytes = alreadyTransferredBytes,
            RemoteFilePath = serverFilePath
        };

        await SendCommandAsync(startCommand);
        Logger.Info($"{ServerMark} 请求下载文件：{serverFilePath} -> {localSaveDirectory}，已传输：{alreadyTransferredBytes}字节");
    }

    /// <summary>
    /// 处理文件传输响应（内部方法，在独立线程中运行）
    /// </summary>
    private async Task ProcessingFileTransferResponsesAsync()
    {
        await foreach (var command in _fileTransferResponses.Reader.ReadAllAsync())
        {
            try
            {
                if (command.IsCommand<FileUploadStartAck>())
                {
                    var ack = command.GetCommand<FileUploadStartAck>();
                    await HandleFileUploadStartAckCommandAsync(ack);
                }
                else if (command.IsCommand<FileBlockAck>())
                {
                    var blockAck = command.GetCommand<FileBlockAck>();
                    await HandleFileBlockAckCommandAsync(blockAck);
                }
                else if (command.IsCommand<FileDownloadStart>())
                {
                    var startInfo = command.GetCommand<FileDownloadStart>();
                    await HandleFileDownloadStartCommandAsync(startInfo);
                }
                else if (command.IsCommand<FileDownloadStartAck>())
                {
                    var ack = command.GetCommand<FileDownloadStartAck>();
                    await HandleFileDownloadStartAckCommandAsync(ack);
                }
                else if (command.IsCommand<FileBlockData>())
                {
                    var blockData = command.GetCommand<FileBlockData>();
                    await HandleFileBlockDataAsync(blockData);
                }
                else if (command.IsCommand<FileTransferReject>())
                {
                    var reject = command.GetCommand<FileTransferReject>();
                    HandleFileTransferRejectCommand(reject);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{ServerMark} 处理文件传输响应异常", ex,
                    uiContent: $"{ServerMark} 处理文件传输响应异常，详细信息请查看日志文件");
            }
        }
    }


    /// <summary>
    /// 处理服务端下载请求（内部方法）- 服务端请求客户端接收文件
    /// </summary>
    private async Task HandleFileDownloadStartCommandAsync(FileDownloadStart startInfo)
    {
        var localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            startInfo.FileName);

        var context = new DownloadContext
        {
            FileName = startInfo.FileName,
            FileSize = startInfo.FileSize,
            FileHash = startInfo.FileHash,
            LocalFilePath = localPath,
            AlreadyTransferredBytes = startInfo.AlreadyTransferredBytes
        };
        _downloadContexts[startInfo.RemoteFilePath] = context;

        var ack = new FileDownloadStartAck
        {
            Accept = true,
            AlreadyTransferredBytes = startInfo.AlreadyTransferredBytes,
            RemoteFilePath = startInfo.RemoteFilePath
        };
        await SendCommandAsync(ack);
        Logger.Info(
            $"{ServerMark} 收到服务端下载请求：{startInfo.FileName}，已传输：{startInfo.AlreadyTransferredBytes}字节，等待服务端发送文件块...");
    }

    /// <summary>
    /// 处理文件上传开始应答命令（内部方法）
    /// </summary>
    private async Task HandleFileUploadStartAckCommandAsync(FileUploadStartAck ack)
    {
        if (_uploadContexts.TryGetValue(ack.RemoteFilePath, out var context))
        {
            context.AlreadyTransferredBytes = ack.AlreadyTransferredBytes;
            Logger.Info($"{ServerMark} 收到服务端上传响应：已传输{ack.AlreadyTransferredBytes}字节，开始发送文件块...");
            await SendUploadBlockAsync(context.LocalFilePath, context.RemoteFilePath, context.FileSize,
                context.FileName, context.AlreadyTransferredBytes);
        }
        else
        {
            Logger.Warn($"{ServerMark} 收到服务端上传响应但无上传会话，忽略");
        }
    }

    /// <summary>
    /// 处理文件下载开始应答命令（内部方法）- 服务端确认下载，开始发送文件块
    /// </summary>
    private async Task HandleFileDownloadStartAckCommandAsync(FileDownloadStartAck ack)
    {
        if (_downloadContexts.TryGetValue(ack.RemoteFilePath, out var context))
        {
            context.AlreadyTransferredBytes = ack.AlreadyTransferredBytes;
            context.FileSize = ack.FileSize > 0 ? ack.FileSize : context.FileSize;
            context.FileHash = ack.FileHash;
            Logger.Info(
                $"{ServerMark} 收到服务端下载响应：文件大小{context.FileSize}字节，已传输{ack.AlreadyTransferredBytes}字节，等待接收文件块...");
        }
        else
        {
            Logger.Warn($"{ServerMark} 收到服务端下载响应但无下载会话，忽略");
        }
    }

    /// <summary>
    /// 处理文件块传输应答命令（内部方法）
    /// </summary>
    private async Task HandleFileBlockAckCommandAsync(FileBlockAck blockAck)
    {
        if (blockAck.Success && _uploadContexts.TryGetValue(blockAck.RemoteFilePath, out var context))
        {
            var nextOffset = blockAck.AlreadyTransferredBytes;
            await SendUploadBlockAsync(context.LocalFilePath, context.RemoteFilePath, context.FileSize,
                context.FileName, nextOffset);
        }
        else if (!blockAck.Success)
        {
            Logger.Error($"{ServerMark} 服务器报告文件块传输失败：{blockAck.Message}");
        }
    }

    /// <summary>
    /// 处理文件传输拒绝命令（内部方法）
    /// </summary>
    private void HandleFileTransferRejectCommand(FileTransferReject reject)
    {
        var errorMessage = reject.ErrorCode switch
        {
            -31 => $"服务端文件大于客户端已有文件",
            -32 => $"文件已存在，无需重复上传",
            -33 => $"文件大小相同但Hash不同，拒绝上传",
            -41 => $"服务端文件不存在",
            -42 => $"服务端文件小于客户端已有文件",
            -43 => $"文件相同，不需要下载",
            -44 => $"文件大小相同但Hash不同",
            _ => $"未知错误：{reject.ErrorCode}，{reject.Message}"
        };

        Logger.Warn($"{ServerMark} 文件传输被拒绝：{reject.FileName}，错误码：{reject.ErrorCode}，原因：{errorMessage}");

        if (_uploadContexts.TryGetValue(reject.RemoteFilePath, out var uploadContext))
        {
            FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                reject.FileName,
                uploadContext.AlreadyTransferredBytes,
                uploadContext.FileSize,
                uploadContext.FileSize > 0
                    ? (double)uploadContext.AlreadyTransferredBytes / uploadContext.FileSize * 100
                    : 0,
                true));
            _uploadContexts.TryRemove(reject.RemoteFilePath, out _);
        }
        else if (_downloadContexts.TryGetValue(reject.RemoteFilePath, out var downloadContext))
        {
            FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                reject.FileName,
                downloadContext.AlreadyTransferredBytes,
                downloadContext.FileSize,
                downloadContext.FileSize > 0
                    ? (double)downloadContext.AlreadyTransferredBytes / downloadContext.FileSize * 100
                    : 0,
                false));
            _downloadContexts.TryRemove(reject.RemoteFilePath, out _);
        }
    }

    /// <summary>
    /// 处理接收到的文件块数据（用于下载：客户端接收服务器发送的文件数据）
    /// </summary>
    /// <param name="blockData">文件块数据</param>
    public async Task HandleFileBlockDataAsync(FileBlockData blockData)
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

            await using var fs = new FileStream(remoteFilePath,
                blockData.Offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.Write);
            await fs.WriteAsync(blockData.Data.AsMemory(0, blockData.BlockSize));

            var totalBytes = fs.Length;
            if (_downloadContexts.TryGetValue(remoteFilePath, out var context))
            {
                context.AlreadyTransferredBytes = totalBytes;
                if (context.FileSize == 0 && totalBytes > 0)
                {
                    context.FileSize = totalBytes;
                }

                var progress = context.FileSize > 0 ? (double)totalBytes / context.FileSize * 100 : 0;
                FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                    context.FileName,
                    totalBytes,
                    context.FileSize,
                    progress,
                    false));
            }

            var ack = new FileBlockAck
            {
                BlockIndex = blockData.BlockIndex,
                Success = true,
                RemoteFilePath = remoteFilePath,
                AlreadyTransferredBytes = totalBytes
            };
            await SendCommandAsync(ack);
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
            await SendCommandAsync(ack);
        }
    }


    /// <summary>
    /// 发送上传文件块（内部方法，用于上传时发送文件数据到服务器）
    /// </summary>
    /// <param name="localFilePath">本地文件路径</param>
    /// <param name="remoteFilePath">远程文件路径</param>
    /// <param name="fileSize">文件大小</param>
    /// <param name="fileName">文件名</param>
    /// <param name="alreadyTransferredBytes">已传输字节数</param>
    internal async Task SendUploadBlockAsync(string localFilePath, string remoteFilePath, long fileSize,
        string fileName, long alreadyTransferredBytes)
    {
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
            RemoteFilePath = remoteFilePath
        };

        await SendCommandAsync(blockData);
        Logger.Info($"{ServerMark} 发送文件块({blockIndex})：{bytesRead}字节");

        var newTransferredBytes = alreadyTransferredBytes + bytesRead;
        var progress = fileSize > 0 ? (double)newTransferredBytes / fileSize * 100 : 0;
        FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
            fileName,
            newTransferredBytes,
            fileSize,
            progress,
            true));
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 获取已传输的字节数（用于断点续传）
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
    /// 保存传输进度到本地文件（用于断点续传）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="bytes">已传输的字节数</param>
    private static async Task SaveTransferProgressAsync(string filePath, long bytes)
    {
        var progressFilePath = filePath + ".progress";
        await File.WriteAllTextAsync(progressFilePath, bytes.ToString());
    }
}

internal class UploadContext
{
    public string LocalFilePath { get; set; } = string.Empty;
    public string RemoteFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public long AlreadyTransferredBytes { get; set; }
}

internal class DownloadContext
{
    public string LocalFilePath { get; set; } = string.Empty;
    public string RemoteFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public long AlreadyTransferredBytes { get; set; }
}