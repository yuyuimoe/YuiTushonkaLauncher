namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>Downloads a mod or addon archive, resuming when the host allows it.</summary>
public interface IForgeDownloader
{
    /// <summary>
    /// Downloads <paramref name="url" /> into <paramref name="destinationDirectory" /> as
    /// <paramref name="fileName" />. Interrupted downloads resume from a <c>.part</c> file; the
    /// returned SHA-256 covers the whole file.
    /// </summary>
    Task<ForgeDownloadResult> DownloadAsync(
        Uri url,
        string destinationDirectory,
        string fileName,
        IProgress<ForgeDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Outcome of a download.</summary>
public sealed record ForgeDownloadResult
{
    /// <summary>Absolute path of the finished file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Size in bytes of the finished file.</summary>
    public required long Length { get; init; }

    /// <summary>SHA-256 of the finished file, lowercase hex.</summary>
    public required string Sha256 { get; init; }

    /// <summary>True when the download continued a previous partial file.</summary>
    public bool Resumed { get; init; }
}

/// <summary>Progress of a download.</summary>
public readonly record struct ForgeDownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Completion between 0 and 1, or <c>null</c> when the host sent no length.</summary>
    public double? Fraction
    {
        get { return TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null; }
    }
}
