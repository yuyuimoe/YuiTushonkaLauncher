namespace YuiTushonkaLauncher.Patching;

/// <summary>
/// Replaces a file by renaming a finished temporary over it.
/// </summary>
/// <remarks>
/// Patching must never write into the target itself: the target may be a hard link into the base
/// install, and truncating it would corrupt every instance built from that install (D6). A rename
/// swaps the directory entry instead — the old inode keeps its links — so the write is atomic with
/// respect to a crash and invisible to the base install.
/// </remarks>
public static class AtomicWrite
{
    /// <summary>Computes the hash of <paramref name="temporaryPath" /> and renames it over the target.</summary>
    /// <returns>SHA-256 of the bytes that landed in <paramref name="targetPath" />.</returns>
    public static async Task<string> CommitAsync(string temporaryPath, string targetPath, CancellationToken cancellationToken = default)
    {
        var hash = await FileHash.ComputeAsync(temporaryPath, cancellationToken);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(temporaryPath, targetPath, overwrite: true);

        return hash;
    }

    /// <summary>Removes a temporary file, ignoring the case where it never existed.</summary>
    public static void Discard(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless: the next run overwrites it.
        }
    }
}
