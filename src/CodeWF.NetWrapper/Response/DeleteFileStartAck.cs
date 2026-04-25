using System;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 兼容旧版“删除路径响应”名称，请改用 <see cref="DeletePathResponse"/>。
/// </summary>
[Obsolete("Use DeletePathResponse instead.")]
[NetHead(SocketConstants.DeletePathResponseObjectId, 1)]
public class DeleteFileStartAck : DeletePathResponse
{
}
