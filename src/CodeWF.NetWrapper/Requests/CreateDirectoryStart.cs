using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 创建目录请求 DTO
/// </summary>
[NetHead(SocketConstants.CreateDirectoryStartObjectId, 1)]
public class CreateDirectoryStart : INetObject
{
    /// <summary>
    /// 服务端目录路径
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;
}