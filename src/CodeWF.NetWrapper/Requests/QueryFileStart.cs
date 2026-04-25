using System;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// 兼容旧版“查询文件系统”请求名称，请改用 <see cref="CodeWF.NetWrapper.Requests.BrowseFileSystemRequest"/>。
/// </summary>
[Obsolete("Use BrowseFileSystemRequest instead.")]
[NetHead(SocketConstants.BrowseFileSystemRequestObjectId, 1)]
public class QueryFileStart : CodeWF.NetWrapper.Requests.BrowseFileSystemRequest
{
}
