using System;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 兼容旧版“创建目录”请求名称，请改用 <see cref="CreateDirectoryRequest"/>。
/// </summary>
[Obsolete("Use CreateDirectoryRequest instead.")]
[NetHead(SocketConstants.CreateDirectoryRequestObjectId, 1)]
public class CreateDirectoryStart : CreateDirectoryRequest
{
}
