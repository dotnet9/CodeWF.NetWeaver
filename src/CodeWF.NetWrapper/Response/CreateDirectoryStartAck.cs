using System;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 兼容旧版“创建目录响应”名称，请改用 <see cref="CreateDirectoryResponse"/>。
/// </summary>
[Obsolete("Use CreateDirectoryResponse instead.")]
[NetHead(SocketConstants.CreateDirectoryResponseObjectId, 1)]
public class CreateDirectoryStartAck : CreateDirectoryResponse
{
}
