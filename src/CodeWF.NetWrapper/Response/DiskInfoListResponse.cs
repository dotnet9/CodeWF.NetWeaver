using System;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWrapper.Response;

/// <summary>
/// 兼容旧版“磁盘列表响应”名称，请改用 <see cref="DriveListResponse"/>。
/// </summary>
[Obsolete("Use DriveListResponse instead.")]
[NetHead(SocketConstants.DriveListResponseObjectId, 1)]
public class DiskInfoListResponse : DriveListResponse
{
}
