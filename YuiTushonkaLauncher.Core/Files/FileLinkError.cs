namespace YuiTushonkaLauncher.Core.Files;

/// <summary>Why a link operation failed, mapped from the platform error code.</summary>
public enum FileLinkError
{
    /// <summary>An unknown failure.</summary>
    Unknown,

    /// <summary>Source and destination are on different volumes (hard links cannot cross them).</summary>
    CrossDevice,

    /// <summary>The file system or the OS refused the operation (permissions, unsupported FS).</summary>
    PermissionDenied,

    /// <summary>The file already has the maximum number of links.</summary>
    LinkLimitReached,

    /// <summary>No space left on the device.</summary>
    NoSpace,

    /// <summary>The destination already exists.</summary>
    AlreadyExists,

    /// <summary>The source does not exist.</summary>
    NotFound,
}

/// <summary>A file-system link operation failed.</summary>
public sealed class FileLinkException : Exception
{
    public FileLinkException(FileLinkError error, string message, string? path = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        Path = path;
    }

    /// <summary>The classified reason.</summary>
    public FileLinkError Error { get; }

    /// <summary>The path the operation was about, when known.</summary>
    public string? Path { get; }

    /// <summary>A user-facing explanation with a concrete next step.</summary>
    public string ToUserMessage()
    {
        return Error switch
        {
            FileLinkError.CrossDevice =>
                "Hard links cannot cross volumes. Create the instance on the same disk/partition as the base install.",
            FileLinkError.PermissionDenied =>
                "The file system refused the operation (permissions, or a file system without hard-link support such as FAT32).",
            FileLinkError.LinkLimitReached => "This file already has the maximum number of hard links.",
            FileLinkError.NoSpace => "There is not enough free space on the destination volume.",
            FileLinkError.AlreadyExists => "The destination already exists.",
            FileLinkError.NotFound => "The source file or directory does not exist.",
            _ => "The file system operation failed.",
        };
    }
}
