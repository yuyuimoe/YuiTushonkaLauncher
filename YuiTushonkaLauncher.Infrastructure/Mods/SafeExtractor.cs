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

        using var archive = ModArchiveContent.Open(archivePath);
        var written = 0;

        if (archive.CanOpenEntriesByName)
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    continue;
                }

                await using var source = archive.OpenEntry(entry);
                if (await WriteAsync(root, entry.Key, entry.LinkTarget, source, cancellationToken).ConfigureAwait(false))
                {
                    written++;
                }
            }
        }
        else
        {
            // A solid archive is decompressed once, in order: the reason a mod archive is read forward-only here.
            using var reader = archive.CreateReader();
            while (await reader.MoveToNextEntryAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory)
                {
                    continue;
                }

                await using var source = await reader.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);
                if (await WriteAsync(root, reader.Entry.Key, reader.Entry.LinkTarget, source, cancellationToken).ConfigureAwait(false))
                {
                    written++;
                }
            }
        }

        logger.LogInformation(
            "Extracted {Count} file(s) from {Archive} ({Format}) into {Destination}",
            written,
            Path.GetFileName(archivePath),
            ModArchiveContent.Describe(archive.Format),
            root
        );
        return written;
    }

    /// <summary>Writes one entry, refusing anything that would leave the destination. False when there is nothing to write.</summary>
    private static async Task<bool> WriteAsync(
        string root,
        string? key,
        string? linkTarget,
        Stream source,
        CancellationToken cancellationToken
    )
    {
        var name = key ?? string.Empty;
        if (name.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(linkTarget))
        {
            throw new FileLinkException(
                FileLinkError.PermissionDenied,
                $"Archive entry '{name}' is a symbolic link and was rejected.",
                name
            );
        }

        if (Path.IsPathRooted(name) || name.Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new FileLinkException(
                FileLinkError.PermissionDenied,
                $"Archive entry '{name}' would be written outside the destination and was rejected.",
                name
            );
        }

        var relative = Normalize(name);
        var destination = Path.GetFullPath(Path.Combine(root, relative));

        if (!IsInside(root, destination))
        {
            throw new FileLinkException(
                FileLinkError.PermissionDenied,
                $"Archive entry '{name}' would be written outside the destination and was rejected.",
                name
            );
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        return true;
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
