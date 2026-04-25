using System;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Requests;

/// <summary>
/// 兼容旧版“删除路径”请求名称，请改用 <see cref="DeletePathRequest"/>。
/// </summary>
[Obsolete("Use DeletePathRequest instead.")]
[NetHead(SocketConstants.DeletePathRequestObjectId, 1)]
public class DeleteFileStart : DeletePathRequest
{
}
