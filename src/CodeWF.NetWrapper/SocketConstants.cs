using System;

namespace CodeWF.NetWrapper;

public class SocketConstants
{
    public const ushort FileUploadStartObjectId = 193;

    public const ushort FileUploadStartAckObjectId = 194;

    public const ushort FileBlockDataObjectId = 195;

    public const ushort FileBlockAckObjectId = 196;

    public const ushort FileTransferCompleteObjectId = 197;

    public const ushort CommonSocketResponseObjectId = 198;

    public const ushort HeartbeatObjectId = 199;

    public const ushort BrowseFileSystemRequestObjectId = 200;

    [Obsolete("Use BrowseFileSystemRequestObjectId instead.")]
    public const ushort QueryFileStartObjectId = BrowseFileSystemRequestObjectId;

    public const ushort DirectoryEntryObjectId = 201;

    public const ushort CreateDirectoryRequestObjectId = 202;

    [Obsolete("Use CreateDirectoryRequestObjectId instead.")]
    public const ushort CreateDirectoryStartObjectId = CreateDirectoryRequestObjectId;

    public const ushort CreateDirectoryResponseObjectId = 203;

    [Obsolete("Use CreateDirectoryResponseObjectId instead.")]
    public const ushort CreateDirectoryStartAckObjectId = CreateDirectoryResponseObjectId;

    public const ushort DeletePathRequestObjectId = 204;

    [Obsolete("Use DeletePathRequestObjectId instead.")]
    public const ushort DeleteFileStartObjectId = DeletePathRequestObjectId;

    public const ushort DeletePathResponseObjectId = 205;

    [Obsolete("Use DeletePathResponseObjectId instead.")]
    public const ushort DeleteFileStartAckObjectId = DeletePathResponseObjectId;

    public const ushort FileTransferRejectObjectId = 206;

    public const ushort FileDownloadStartObjectId = 207;

    public const ushort FileDownloadStartAckObjectId = 208;

    public const ushort BrowseFileSystemResponseObjectId = 209;

    [Obsolete("Use BrowseFileSystemResponseObjectId instead.")]
    public const ushort DirectoryEntryResponseObjectId = BrowseFileSystemResponseObjectId;

    public const ushort DiskInfoObjectId = 210;

    public const ushort DriveListResponseObjectId = 211;

    [Obsolete("Use DriveListResponseObjectId instead.")]
    public const ushort DiskInfoListResponseObjectId = DriveListResponseObjectId;
}
