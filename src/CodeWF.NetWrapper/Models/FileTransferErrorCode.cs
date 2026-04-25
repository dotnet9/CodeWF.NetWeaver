using System.ComponentModel;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 文件传输相关错误码。
/// </summary>
public enum FileTransferErrorCode
{
    /// <summary>
    /// 成功。
    /// </summary>
    [Description("成功")]
    Success = 0,

    /// <summary>
    /// 文件不存在。
    /// </summary>
    [Description("文件未找到")]
    FileNotFound = -21,

    /// <summary>
    /// 删除失败。
    /// </summary>
    [Description("删除失败")]
    DeleteFailed = -22,

    /// <summary>
    /// 服务端文件大于客户端已有文件。
    /// </summary>
    [Description("服务端文件大于客户端已有文件")]
    UploadServerFileLarger = -31,

    /// <summary>
    /// 文件已存在，无需重复上传。
    /// </summary>
    [Description("文件已存在，无需重复上传")]
    UploadFileAlreadyExists = -32,

    /// <summary>
    /// 文件大小相同但哈希值不同。
    /// </summary>
    [Description("文件大小相同但哈希值不同")]
    UploadFileHashMismatch = -33,

    /// <summary>
    /// 服务端文件不存在。
    /// </summary>
    [Description("服务端文件不存在")]
    DownloadServerFileNotFound = -41,

    /// <summary>
    /// 服务端文件小于客户端已有文件。
    /// </summary>
    [Description("服务端文件小于客户端已有文件")]
    DownloadServerFileSmaller = -42,

    /// <summary>
    /// 文件相同，不需要下载。
    /// </summary>
    [Description("文件相同，不需要下载")]
    DownloadFileIdentical = -43,

    /// <summary>
    /// 文件大小相同但哈希值不同。
    /// </summary>
    [Description("文件大小相同但哈希值不同")]
    DownloadFileHashMismatch = -44,

    /// <summary>
    /// 目录不存在。
    /// </summary>
    [Description("目录未找到")]
    DirectoryNotFound = -1,

    /// <summary>
    /// 目录访问被拒绝。
    /// </summary>
    [Description("目录访问被拒绝")]
    DirectoryAccessDenied = -2,

    /// <summary>
    /// 未知错误。
    /// </summary>
    [Description("未知错误")]
    UnknownError = -99
}
