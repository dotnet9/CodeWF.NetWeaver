using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeWF.Log.Core;
using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;
using CodeWF.NetWrapper.Models;

namespace CodeWF.NetWrapper.Helpers;

/// <summary>
/// TCP Socket 客户端类，用于与 TCP 服务器建立连接并进行通信，支持文件传输和命令收发
/// </summary>
public class TcpSocketClient
{
    private readonly Channel<SocketCommand> _responses = Channel.CreateUnbounded<SocketCommand>();
    private Socket? _client;

    #region 公开属性

    /// <summary>
    /// 服务标识，用以区分多个服务
    /// </summary>
    public string? ServerMark { get; private set; }

    /// <summary>
    /// 系统ID，用于标识客户端身份
    /// </summary>
    public long SystemId { get; private set; }

    /// <summary>
    /// 服务器IP地址
    /// </summary>
    public string? ServerIP { get; private set; }

    /// <summary>
    /// 服务器端口号
    /// </summary>
    public int ServerPort { get; private set; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 本地端点地址
    /// </summary>
    public string? LocalEndPoint { get; set; }

    /// <summary>
    /// 是否可以发送数据（需已连接且正在运行）
    /// </summary>
    public bool CanSend => _client is { Connected: true } && IsRunning;

    #endregion

    #region 公开接口

    /// <summary>
    /// 连接到TCP服务器
    /// </summary>
    /// <param name="serverMark">服务器标识</param>
    /// <param name="serverIP">服务器IP地址</param>
    /// <param name="serverPort">服务器端口号</param>
    /// <returns>连接结果和错误信息</returns>
    public async Task<(bool IsSuccess, string? ErrorMessage)> ConnectAsync(string serverMark, string serverIP,
        int serverPort)
    {
        ServerMark = serverMark;
        ServerIP = serverIP;
        ServerPort = serverPort;

        try
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _client.ConnectAsync(ipEndPoint);

            IsRunning = true;
            Logger.Info($"{ServerMark} 连接成功，服务地址是： {ServerIP}:{ServerPort}，当前客户端地址：{_client.LocalEndPoint}");

            _ = Task.Run(ListenForServerAsync);
            _ = Task.Run(CheckResponseAsync);

            LocalEndPoint = _client.LocalEndPoint?.ToString();
            return (IsSuccess: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LocalEndPoint = null;
            Logger.Error($"{ServerMark} 连接异常 {ServerIP}:{ServerPort}", ex, $"{ServerMark} 连接异常 {ServerIP}:{ServerPort}，详细信息请查看日志文件");
            return (IsSuccess: false, ErrorMessage: $"{ServerMark} 连接异常 {ServerIP}:{ServerPort}，原因：{ex.Message}");
        }
    }

    /// <summary>
    /// 断开连接并停止客户端
    /// </summary>
    public void Stop()
    {
        IsRunning = false;
        _responses.Writer.Complete();
        _client?.CloseSocket();
        LocalEndPoint = null;
    }

    /// <summary>
    /// 发送命令到服务器
    /// </summary>
    /// <param name="command">要发送的网络对象命令</param>
    /// <exception cref="Exception">未连接时抛出异常</exception>
    public async Task SendCommandAsync(INetObject command)
    {
        var netObjInfo = command.GetType().GetNetObjectHead();
        if (!CanSend) throw new Exception($"{ServerMark} 未连接，无法发送命令【ID：{netObjInfo.Id}，Version：{netObjInfo.Version}】");

        var buffer = command.Serialize(SystemId);
        await _client!.SendAsync(buffer);
    }

    #endregion

    #region 连接TCP、接收数据

    /// <summary>
    /// 监听服务器消息（内部方法）
    /// </summary>
    private async Task ListenForServerAsync()
    {
        while (IsRunning && _client?.Connected == true)
        {
            try
            {
                var (success, buffer, headInfo) = await _client!.ReadPacketAsync();
                if (!success) break;

                SystemId = headInfo.SystemId;
                await _responses.Writer.WriteAsync(new SocketCommand(headInfo, buffer, _client));
            }
            catch (SocketException ex)
            {
                var msg = $"{ServerMark} 处理接收数据异常，当前客户端地址：{_client?.LocalEndPoint}";
                Logger.Error(msg, ex, $"{msg}，详细信息请查看日志文件");
                await EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(this, msg));
                break;
            }
            catch (Exception ex)
            {
                var msg = $"{ServerMark} 处理接收数据异常，当前客户端地址：{_client?.LocalEndPoint}";
                if (IsRunning)
                {
                    Logger.Error(msg, ex, $"{msg}，详细信息请查看日志文件");
                }
                await EventBus.EventBus.Default.PublishAsync(new TcpClientErrorCommand(this, msg));
                break;
            }
        }
    }

    /// <summary>
    /// 检查响应队列并发布事件（内部方法）
    /// </summary>
    private async Task CheckResponseAsync()
    {
        await foreach (var command in _responses.Reader.ReadAllAsync())
        {
            await EventBus.EventBus.Default.PublishAsync(command);
        }
    }

    #endregion

    #region 文件传输接口（断点续传）

    /// <summary>
    /// 文件传输块大小（64KB），每次传输的数据块大小
    /// </summary>
    public const int FileTransferBlockSize = 64 * 1024;

    /// <summary>
    /// 文件传输进度事件，当文件传输进度更新时触发
    /// </summary>
    public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgress;

    private FileTransferSession? _currentDownloadSession;
    private FileTransferSession? _currentUploadSession;

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
        _currentUploadSession = new FileTransferSession
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            LocalFilePath = localFilePath,
            IsUpload = true
        };

        _currentUploadSession.AlreadyTransferredBytes = GetExistingTransferBytes(remoteFilePath);
        _currentUploadSession.FileHash = await ComputeFileHashAsync(localFilePath);

        var startCommand = new FileTransferStart
        {
            FileName = remoteFileName,
            FileSize = fileInfo.Length,
            FileHash = _currentUploadSession.FileHash,
            AlreadyTransferredBytes = _currentUploadSession.AlreadyTransferredBytes,
            IsUpload = true,
            RemoteFilePath = remoteFilePath
        };

        await SendCommandAsync(startCommand);
        Logger.Info($"{ServerMark} 请求上传文件：{localFilePath} -> {remoteFilePath}，大小：{fileInfo.Length}字节，已传输：{_currentUploadSession.AlreadyTransferredBytes}字节");

        await SendUploadBlockAsync();
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

        _currentDownloadSession = new FileTransferSession
        {
            FileName = serverFileName,
            FileSize = 0,
            FileHash = string.Empty,
            AlreadyTransferredBytes = GetExistingTransferBytes(serverFilePath),
            IsUpload = false,
            LocalFilePath = Path.Combine(localSaveDirectory, serverFileName)
        };

        var startCommand = new FileTransferStart
        {
            FileName = serverFileName,
            FileSize = 0,
            FileHash = string.Empty,
            AlreadyTransferredBytes = _currentDownloadSession.AlreadyTransferredBytes,
            IsUpload = false,
            RemoteFilePath = serverFilePath
        };

        await SendCommandAsync(startCommand);
        Logger.Info($"{ServerMark} 请求下载文件：{serverFilePath} -> {localSaveDirectory}，已传输：{_currentDownloadSession.AlreadyTransferredBytes}字节");
    }

    /// <summary>
    /// 处理接收到的文件块数据（用于下载：客户端接收服务器发送的文件数据）
    /// </summary>
    /// <param name="blockIndex">块索引号</param>
    /// <param name="offset">数据偏移量</param>
    /// <param name="blockSize">数据块大小</param>
    /// <param name="data">数据内容</param>
    public async Task HandleFileBlockDataAsync(long blockIndex, long offset, int blockSize, byte[] data)
    {
        if (_currentDownloadSession == null)
        {
            Logger.Error($"{ServerMark} 未开始文件下载，无法处理文件块");
            return;
        }

        try
        {
            _currentDownloadSession.LocalFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                _currentDownloadSession.FileName);

            var directory = Path.GetDirectoryName(_currentDownloadSession.LocalFilePath) ?? ".";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fs = new FileStream(_currentDownloadSession.LocalFilePath, FileMode.Append, FileAccess.Write, FileShare.Write);
            await fs.WriteAsync(data.AsMemory(0, blockSize));

            var ack = new FileBlockAck
            {
                BlockIndex = blockIndex,
                Success = true
            };
            await SendCommandAsync(ack);

            _currentDownloadSession.AlreadyTransferredBytes += blockSize;
            if (_currentDownloadSession.FileSize > 0)
            {
                var progress = (double)_currentDownloadSession.AlreadyTransferredBytes / _currentDownloadSession.FileSize * 100;
                FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
                    _currentDownloadSession.FileName,
                    _currentDownloadSession.AlreadyTransferredBytes,
                    _currentDownloadSession.FileSize,
                    progress,
                    false));
            }

            if (_currentDownloadSession.AlreadyTransferredBytes >= _currentDownloadSession.FileSize && _currentDownloadSession.FileSize > 0)
            {
                _currentDownloadSession = null;
                Logger.Info($"{ServerMark} 文件下载完成");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"{ServerMark} 处理文件块({blockIndex})异常", ex);
            var ack = new FileBlockAck
            {
                BlockIndex = blockIndex,
                Success = false,
                Message = ex.Message
            };
            await SendCommandAsync(ack);
        }
    }

    /// <summary>
    /// 处理服务器发起的文件传输开始请求（用于上传：客户端作为发送端）
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileSize">文件大小</param>
    /// <param name="fileHash">文件哈希</param>
    /// <param name="alreadyTransferredBytes">已传输字节数（断点续传用）</param>
    /// <param name="remoteFilePath">远程文件路径（服务端保存路径）</param>
    public async Task HandleFileTransferStartAsync(string fileName, long fileSize, string fileHash, long alreadyTransferredBytes, string remoteFilePath)
    {
        _currentDownloadSession = new FileTransferSession
        {
            FileName = fileName,
            FileSize = fileSize,
            FileHash = fileHash,
            LocalFilePath = remoteFilePath,
            IsUpload = true,
            AlreadyTransferredBytes = alreadyTransferredBytes
        };

        var ack = new FileTransferStartAck
        {
            Accept = true,
            AlreadyTransferredBytes = alreadyTransferredBytes
        };
        await SendCommandAsync(ack);
        Logger.Info($"{ServerMark} 收到服务器文件传输请求：{fileName} -> {remoteFilePath}，已传输：{alreadyTransferredBytes}字节");

        await RequestNextBlockAsync();
    }

    /// <summary>
    /// 请求下一个文件块（内部方法，用于下载时请求服务器发送文件数据）
    /// </summary>
    internal async Task RequestNextBlockAsync()
    {
        if (_currentDownloadSession == null || !_currentDownloadSession.IsUpload)
        {
            if (_currentDownloadSession != null)
            {
                var requestCommand = new FileBlockData
                {
                    BlockIndex = 0,
                    Offset = _currentDownloadSession.AlreadyTransferredBytes,
                    BlockSize = FileTransferBlockSize
                };
                await SendCommandAsync(requestCommand);
            }
        }
        else if (_currentUploadSession != null && _currentUploadSession.IsUpload)
        {
            await SendUploadBlockAsync();
        }
    }

    /// <summary>
    /// 发送上传文件块（内部方法，用于上传时发送文件数据到服务器）
    /// </summary>
    internal async Task SendUploadBlockAsync()
    {
        if (_currentUploadSession == null || !_currentUploadSession.IsUpload)
        {
            return;
        }

        var fileInfo = new FileInfo(_currentUploadSession.LocalFilePath);
        if (!fileInfo.Exists)
        {
            Logger.Error($"{ServerMark} 文件不存在：{_currentUploadSession.LocalFilePath}");
            return;
        }

        using var fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Position = _currentUploadSession.AlreadyTransferredBytes;

        var blockSize = (int)Math.Min(FileTransferBlockSize, _currentUploadSession.FileSize - _currentUploadSession.AlreadyTransferredBytes);
        var buffer = new byte[blockSize];
        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, blockSize));

        if (bytesRead == 0)
        {
            return;
        }

        var blockIndex = _currentUploadSession.AlreadyTransferredBytes / FileTransferBlockSize;
        var blockData = new FileBlockData
        {
            BlockIndex = blockIndex,
            Offset = _currentUploadSession.AlreadyTransferredBytes,
            BlockSize = bytesRead,
            Data = bytesRead == blockSize ? buffer : buffer.AsSpan(0, bytesRead).ToArray()
        };

        await SendCommandAsync(blockData);

        _currentUploadSession.AlreadyTransferredBytes += bytesRead;
        var progress = (double)_currentUploadSession.AlreadyTransferredBytes / _currentUploadSession.FileSize * 100;
        FileTransferProgress?.Invoke(this, new FileTransferProgressEventArgs(
            _currentUploadSession.FileName,
            _currentUploadSession.AlreadyTransferredBytes,
            _currentUploadSession.FileSize,
            progress,
            true));

        if (_currentUploadSession.AlreadyTransferredBytes >= _currentUploadSession.FileSize)
        {
            SaveTransferProgress(_currentUploadSession.FileName, _currentUploadSession.AlreadyTransferredBytes);
            _currentUploadSession = null;
            Logger.Info($"{ServerMark} 文件上传完成");
        }
    }

    /// <summary>
    /// 处理文件块传输应答（用于上传：客户端发送文件块后等待服务器确认）
    /// </summary>
    /// <param name="blockIndex">块索引号</param>
    /// <param name="success">是否成功</param>
    /// <param name="message">消息内容</param>
    public async Task HandleFileBlockAckAsync(long blockIndex, bool success, string message)
    {
        if (success)
        {
            if (_currentUploadSession != null && _currentUploadSession.IsUpload)
            {
                await SendUploadBlockAsync();
            }
            else if (_currentDownloadSession != null && !_currentDownloadSession.IsUpload)
            {
                await RequestNextBlockAsync();
            }
        }
        else
        {
            Logger.Error($"{ServerMark} 服务器报告文件块传输失败：{message}");
        }
    }

    private long GetExistingTransferBytes(string fileName)
    {
        var progressFile = GetProgressFilePath(fileName);
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

    private void SaveTransferProgress(string fileName, long totalBytes)
    {
        var progressFile = GetProgressFilePath(fileName);
        File.WriteAllText(progressFile, totalBytes.ToString());
    }

    private static string GetProgressFilePath(string fileName) =>
        Path.Combine(Path.GetTempPath(), $"file_transfer_client_{fileName}.progress");

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    #endregion
}