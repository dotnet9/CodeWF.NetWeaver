namespace CodeWF.NetWrapper.Models;

/// <summary>
///     Identifies the requested file-system operation for authorization callbacks.
/// </summary>
public enum FileSystemOperation
{
    Browse = 0,
    CreateDirectory = 1,
    Delete = 2,
    Upload = 3,
    Download = 4
}
