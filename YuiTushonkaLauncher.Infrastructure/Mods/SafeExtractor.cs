using System.IO.Compression;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Mods;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Extracts an archive into a staging directory, refusing any entry that would escape it.
/// </summary>
/// <remarks>
/// Nothing is written outside <c>destinationDirectory</c>: absolute paths, <c>..</c> segments, drive
/// letters and links are rejected before a single byte is written, and the final path is verified to be
/// inside the destination even after normalisation.
/// </remarks>
public sealed class SafeExtractor(ILogger<SafeExtractor> logger) : IArchiveExtractor
{
    /// <inheritdoc />
    public async Task<int> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(archivePath);
        var written = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (Path.IsPathRooted(entry.FullName) || entry.FullName.Split('/', '\\').Any(segment => segment == ".."))
            {
                throw new FileLinkException(
                    FileLinkError.PermissionDenied,
                    $"Archive entry '{entry.FullName}' would be written outside the destination and was rejected.",
                    entry.FullName
                );
            }

            var relative = Normalize(entry.FullName);
            var destination = Path.GetFullPath(Path.Combine(root, relative));

            if (!IsInside(root, destination))
            {
                throw new FileLinkException(
                    FileLinkError.PermissionDenied,
                    $"Archive entry '{entry.FullName}' would be written outside the destination and was rejected.",
                    entry.FullName
                );
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await using var source = entry.Open();
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(target, cancellationToken);
            written++;
        }

        logger.LogInformation("Extracted {Count} file(s) from {Archive} into {Destination}", written, Path.GetFileName(archivePath), root);
        return written;
    }

    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static string Normalize(string fullName)
    {
        return fullName.Replace('\\', '/').TrimStart('/');
    }
}
