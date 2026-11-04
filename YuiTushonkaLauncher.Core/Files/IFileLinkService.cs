namespace YuiTushonkaLauncher.Core.Files;

/// <summary>
/// The only way the launcher touches instance files. Every operation states whether it shares or
/// copies content, so the copy-on-write invariant has exactly one owner.
/// </summary>
public interface IFileLinkService
{
    /// <summary>Creates a hard link at <paramref name="linkPath" /> pointing at <paramref name="existingPath" />.</summary>
    /// <exception cref="FileLinkException">The platform refused the link.</exception>
    void CreateHardLink(string linkPath, string existingPath);

    /// <summary>Creates a symbolic link.</summary>
    void CreateSymbolicLink(string linkPath, string existingPath, bool isDirectory);

    /// <summary>Number of hard links to the file, or 1 when it cannot be determined.</summary>
    int GetLinkCount(string path);

    /// <summary>True when both paths live on the same volume (a hard link is possible).</summary>
    bool IsSameVolume(string pathA, string pathB);

    /// <summary>Bytes available on the volume that holds <paramref name="path" />, or <c>null</c> when unknown.</summary>
    long? GetAvailableFreeSpace(string path);

    /// <summary>
    /// Copy-on-write: if <paramref name="path" /> is shared through a hard link, replaces it with a
    /// private copy (same content, new inode) so a later write cannot affect the other links.
    /// Does nothing when the file is missing or unshared.
    /// </summary>
    void Detach(string path);

    /// <summary>Copies a single file, preserving the Unix mode when possible.</summary>
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    /// <summary>Copies a directory tree (files copied, not linked).</summary>
    Task CopyDirectoryAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken = default);
}
