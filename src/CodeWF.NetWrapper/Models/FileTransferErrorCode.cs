using System.ComponentModel;

namespace CodeWF.NetWrapper.Models;

public enum FileTransferErrorCode
{
    [Description("成功")]
    Success = 0,

    [Description("文件未找到")]
    FileNotFound = -21,

    [Description("删除失败")]
    DeleteFailed = -22,

    [Description("服务端文件大于客户端已有文件")]
    UploadServerFileLarger = -31,

    [Description("文件已存在，无需重复上传")]
    UploadFileAlreadyExists = -32,

    [Description("文件大小相同但Hash不同")]
    UploadFileHashMismatch = -33,

    [Description("服务端文件不存在")]
    DownloadServerFileNotFound = -41,

    [Description("服务端文件小于客户端已有文件")]
    DownloadServerFileSmaller = -42,

    [Description("文件相同，不需要下载")]
    DownloadFileIdentical = -43,

    [Description("文件大小相同但Hash不同")]
    DownloadFileHashMismatch = -44,

    [Description("目录未找到")]
    DirectoryNotFound = -1,

    [Description("目录访问被拒绝")]
    DirectoryAccessDenied = -2,

    [Description("未知错误")]
    UnknownError = -99
}