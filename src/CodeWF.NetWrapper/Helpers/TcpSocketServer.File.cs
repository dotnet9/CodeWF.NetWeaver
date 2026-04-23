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
        var directoryPath = queryInfo.DirectoryPath;

        if (!Directory.Exists(directoryPath))
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})查询目录不存在：{directoryPath}");
            var reject = new FileTransferReject
            {
                ErrorCode = -1,
                Message = "目录不存在"
            };
            await SendCommandAsync(client, reject);
            return;
        }

        try
        {
            var entries = Directory.GetFileSystemEntries(directoryPath);
            var directoryEntries = new List<DirectoryEntry>();

            foreach (var entry in entries)
            {
                var info = new FileInfo(entry);
                directoryEntries.Add(new DirectoryEntry
                {
                    Name = info.Name,
                    Size = info.Exists ? info.Length : 0,
                    LastModifiedTime = info.Exists ? info.LastWriteTime.Ticks : DateTime.Now.Ticks,
                    IsDirectory = info.Attributes.HasFlag(FileAttributes.Directory)
                });
            }

            const int batchSize = 100;
            for (var i = 0; i < directoryEntries.Count; i += batchSize)
            {
                var batch = directoryEntries.Skip(i).Take(batchSize).ToList();
                foreach (var entry in batch)
                {
                    await SendCommandAsync(client, entry);
                }
            }

            Logger.Info($"{ServerMark} 客户端({clientKey})查询目录成功：{directoryPath}，共{directoryEntries.Count}个条目");
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 客户端({clientKey})查询目录异常：{directoryPath}", ex);
            var reject = new FileTransferReject
            {
                ErrorCode = -2,
                Message = ex.Message
            };
            await SendCommandAsync(client, reject);
        }
    }


    /// <summary>
    /// 处理创建目录请求
    /// </summary>
    private async Task HandleCreateDirectoryStartAsync(Socket client, CreateDirectoryStart createInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var directoryPath = createInfo.DirectoryPath;

        if (Directory.Exists(directoryPath))
        {
            var ack = new CreateDirectoryStartAck
            {
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
            var reject = new FileTransferReject
            {
                ErrorCode = -11,
                Message = ex.Message
            };
            await SendCommandAsync(client, reject);
        }
    }

    /// <summary>
    /// 处理删除文件或目录请求
    /// </summary>
    private async Task HandleDeleteFileStartAsync(Socket client, DeleteFileStart deleteInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var filePath = deleteInfo.FilePath;

        if (deleteInfo.IsDirectory)
        {
            if (!Directory.Exists(filePath))
            {
                var reject = new FileTransferReject
                {
                    ErrorCode = -22,
                    Message = "目录不存在"
                };
                await SendCommandAsync(client, reject);
                Logger.Warn($"{ServerMark} 客户端({clientKey})删除目录不存在：{filePath}");
                return;
            }

            try
            {
                Directory.Delete(filePath, false);
                var ack = new DeleteFileStartAck
                {
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
                var reject = new FileTransferReject
                {
                    ErrorCode = -21,
                    Message = ex.Message
                };
                await SendCommandAsync(client, reject);
            }
        }
        else
        {
            if (!File.Exists(filePath))
            {
                var reject = new FileTransferReject
                {
                    ErrorCode = -22,
                    Message = "文件不存在"
                };
                await SendCommandAsync(client, reject);
                Logger.Warn($"{ServerMark} 客户端({clientKey})删除文件不存在：{filePath}");
                return;
            }

            try
            {
                File.Delete(filePath);
                var ack = new DeleteFileStartAck
                {
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
                var reject = new FileTransferReject
                {
                    ErrorCode = -21,
                    Message = ex.Message
                };
                await SendCommandAsync(client, reject);
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
        var remoteFilePath = startInfo.RemoteFilePath;
        var alreadyTransferredBytes = startInfo.AlreadyTransferredBytes;

        var actualTransferredBytes = 0L;
        if (File.Exists(remoteFilePath))
        {
            var fileInfo = new FileInfo(remoteFilePath);
            actualTransferredBytes = fileInfo.Length;
        }

        var ack = new FileUploadStartAck
        {
            Accept = true,
            AlreadyTransferredBytes = actualTransferredBytes,
            RemoteFilePath = remoteFilePath
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


    /// <summary>
    /// 处理客户端下载文件请求
    /// </summary>
    /// <param name="client">客户端 Socket</param>
    /// <param name="startInfo">文件下载开始信息</param>
    public async Task HandleFileDownloadStartAsync(Socket client, FileDownloadStart startInfo)
    {
        var clientKey = client.RemoteEndPoint?.ToString() ?? string.Empty;
        var remoteFilePath = startInfo.RemoteFilePath;
        var alreadyTransferredBytes = startInfo.AlreadyTransferredBytes;

        if (!File.Exists(remoteFilePath))
        {
            Logger.Error($"{ServerMark} 文件不存在：{remoteFilePath}");
            return;
        }

        var fileInfo = new FileInfo(remoteFilePath);
        var totalFileSize = fileInfo.Length;

        Logger.Info($"{ServerMark} 收到客户端({clientKey})下载请求：{remoteFilePath}，已传输：{alreadyTransferredBytes}字节，开始发送文件...");

        while (alreadyTransferredBytes < totalFileSize)
        {
            await using var fs = new FileStream(remoteFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Position = alreadyTransferredBytes;

            var blockSize = (int)Math.Min(FileTransferBlockSize, totalFileSize - alreadyTransferredBytes);
            if (blockSize <= 0)
            {
                break;
            }

            var buffer = new byte[blockSize];
            var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, blockSize));
            if (bytesRead == 0)
            {
                break;
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

            await SendCommandAsync(client, blockData);
            Logger.Info($"{ServerMark} 向客户端({clientKey})发送文件块({blockIndex})：{bytesRead}字节");

            alreadyTransferredBytes += bytesRead;
        }

        Logger.Info($"{ServerMark} 向客户端({clientKey})发送文件完成：{startInfo.FileName}");
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