namespace YuiTushonkaLauncher.Core.Files;

/// <summary>
/// Writes into an instance after making sure the target is not shared. Every writer in the launcher
/// goes through here; nothing else may call <see cref="File.WriteAllText(string, string)" /> on an
/// instance path.
/// </summary>
public interface ILinkAwareFileWriter
{
    /// <summary>Detaches the target (when needed), then writes the bytes.</summary>
    Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>Detaches the target (when needed), then writes the text as UTF-8 without BOM.</summary>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches the destination (when it exists and <paramref name="overwrite" /> is set), then copies
    /// the source over it.
    /// </summary>
    Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default);
}
