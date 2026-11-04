using YuiTushonkaLauncher.Core.Files;

namespace YuiTushonkaLauncher.Infrastructure.FileSystem;

/// <summary>
/// Turns raw platform error codes into <see cref="FileLinkError" /> values, so callers can react to
/// "wrong volume" and "no space" instead of parsing messages.
/// </summary>
public static class PlatformErrorMapper
{
    /// <summary>Maps a Unix <c>errno</c>.</summary>
    public static FileLinkError FromUnix(int errno)
    {
        return errno switch
        {
            18 => FileLinkError.CrossDevice, // EXDEV
            1 or 13 or 30 => FileLinkError.PermissionDenied, // EPERM, EACCES, EROFS
            31 => FileLinkError.LinkLimitReached, // EMLINK
            28 => FileLinkError.NoSpace, // ENOSPC
            17 => FileLinkError.AlreadyExists, // EEXIST
            2 => FileLinkError.NotFound, // ENOENT
            _ => FileLinkError.Unknown,
        };
    }

    /// <summary>Maps a Win32 error code.</summary>
    public static FileLinkError FromWindows(int error)
    {
        return error switch
        {
            17 => FileLinkError.CrossDevice, // ERROR_NOT_SAME_DEVICE
            5 or 19 or 32 => FileLinkError.PermissionDenied, // ACCESS_DENIED, WRITE_PROTECT, SHARING_VIOLATION
            1142 => FileLinkError.LinkLimitReached, // ERROR_TOO_MANY_LINKS
            112 => FileLinkError.NoSpace, // ERROR_DISK_FULL
            183 => FileLinkError.AlreadyExists, // ERROR_ALREADY_EXISTS
            2 or 3 => FileLinkError.NotFound, // FILE_NOT_FOUND, PATH_NOT_FOUND
            1 => FileLinkError.PermissionDenied, // ERROR_INVALID_FUNCTION: file system without hard links
            _ => FileLinkError.Unknown,
        };
    }
}
