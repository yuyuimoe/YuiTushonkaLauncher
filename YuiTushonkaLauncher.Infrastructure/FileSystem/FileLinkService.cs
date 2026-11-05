using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Files;

namespace YuiTushonkaLauncher.Infrastructure.FileSystem;

/// <summary>
/// <see cref="IFileLinkService" /> over the platform calls in <see cref="FileSystemNative" />.
/// </summary>
public sealed class FileLinkService(ILogger<FileLinkService> logger) : IFileLinkService
{
    /// <inheritdoc />
    public void CreateHardLink(string linkPath, string existingPath)
    {
        var parent = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var error = FileSystemNative.TryCreateHardLink(existingPath, linkPath);
        if (error != 0)
        {
            throw BuildException(error, existingPath, linkPath);
        }
    }

    /// <inheritdoc />
    public void CreateSymbolicLink(string linkPath, string existingPath, bool isDirectory)
    {
        var parent = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (isDirectory)
        {
            Directory.CreateSymbolicLink(linkPath, existingPath);
            return;
        }

        File.CreateSymbolicLink(linkPath, existingPath);
    }

    /// <inheritdoc />
    public int GetLinkCount(string path)
    {
        return FileSystemNative.TryGetFileIdentity(path, out var linkCount, out _) ? linkCount : 1;
    }

    /// <inheritdoc />
    public bool IsSameVolume(string pathA, string pathB)
    {
        if (!FileSystemNative.TryGetFileIdentity(pathA, out _, out var deviceA))
        {
            return true;
        }

        if (!FileSystemNative.TryGetFileIdentity(pathB, out _, out var deviceB))
        {
            return true;
        }

        return string.Equals(deviceA, deviceB, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public long? GetAvailableFreeSpace(string path)
    {
        return FileSystemNative.TryGetAvailableFreeSpace(path);
    }

    /// <inheritdoc />
    public void Detach(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (GetLinkCount(path) <= 1)
        {
            return;
        }

        // Copy next to the target, then swap: a crash cannot leave a truncated instance file.
        var directory = Path.GetDirectoryName(path) ?? ".";
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():n}.detach");

        try
        {
            File.Copy(path, temporary, overwrite: true);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            }

            if (OperatingSystem.IsWindows() && new FileInfo(path).IsReadOnly)
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.Move(temporary, path, overwrite: true);
            logger.LogDebug("Detached {Path} into a private copy", path);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(sourcePath, destinationPath, overwrite);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
        }
    }

    /// <inheritdoc />
    public async Task CopyDirectoryAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            CopyFile(file, target, overwrite: true);
            await Task.Yield();
        }
    }

    private FileLinkException BuildException(int error, string existingPath, string linkPath)
    {
        var mapped = OperatingSystem.IsWindows() ? PlatformErrorMapper.FromWindows(error) : PlatformErrorMapper.FromUnix(error);
        logger.LogDebug("Hard link {LinkPath} -> {ExistingPath} failed with {Error} ({ErrorCode})", linkPath, existingPath, mapped, error);

        return new FileLinkException(mapped, $"Could not hard link '{existingPath}' to '{linkPath}' (error {error}).", linkPath);
    }
}
