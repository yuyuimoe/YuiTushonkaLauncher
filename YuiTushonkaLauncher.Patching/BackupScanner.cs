using YuiTushonkaLauncher.Core.Patching;

namespace YuiTushonkaLauncher.Patching;

/// <summary>One backup found on disk, with the file it restores.</summary>
public sealed record BackupPair
{
    /// <summary>Absolute path of the <c>.spt-bak</c> file.</summary>
    public required string BackupPath { get; init; }

    /// <summary>Absolute path of the file the backup restores.</summary>
    public required string TargetPath { get; init; }

    /// <summary>Instance-relative path of the target, for logs and reports.</summary>
    public required string RelativePath { get; init; }
}

/// <summary>
/// Finds the <c>.spt-bak</c> backups an instance holds.
/// </summary>
/// <remarks>
/// Reparse points are skipped: a symlink or junction would make the walk escape the instance (or loop
/// through it), and restoring through one would write outside the instance.
/// </remarks>
public sealed class BackupScanner
{
    /// <summary>Enumerates every backup under <paramref name="instanceRoot" />.</summary>
    public IReadOnlyList<BackupPair> Discover(string instanceRoot)
    {
        if (!Directory.Exists(instanceRoot))
        {
            return [];
        }

        var pairs = new List<BackupPair>();
        var pending = new Stack<string>();
        pending.Push(instanceRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(directory, "*" + DeltaSetLocation.BackupSuffix, SearchOption.TopDirectoryOnly))
            {
                var target = file[..^DeltaSetLocation.BackupSuffix.Length];
                pairs.Add(
                    new BackupPair
                    {
                        BackupPath = file,
                        TargetPath = target,
                        RelativePath = Path.GetRelativePath(instanceRoot, target).Replace('\\', '/'),
                    }
                );
            }

            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pending.Push(child);
            }
        }

        pairs.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));

        return pairs;
    }

    /// <summary>
    /// Restores one backup over its target, then deletes the backup.
    /// </summary>
    /// <remarks>
    /// The backup is copied to a temporary file, hashed, renamed over the target and only then removed:
    /// a crash or a bad copy leaves the backup in place, so the restore can be retried.
    /// </remarks>
    public static async Task<string> RestoreAsync(BackupPair pair, CancellationToken cancellationToken = default)
    {
        var temporary = pair.TargetPath + ".spt-restore";
        AtomicWrite.Discard(temporary);

        try
        {
            File.Copy(pair.BackupPath, temporary, overwrite: true);

            var expected = await FileHash.ComputeAsync(pair.BackupPath, cancellationToken);
            var restored = await AtomicWrite.CommitAsync(temporary, pair.TargetPath, cancellationToken);

            if (!string.Equals(expected, restored, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Restoring '{pair.RelativePath}' produced {restored}, but the backup is {expected}. The backup was kept."
                );
            }

            File.Delete(pair.BackupPath);

            return restored;
        }
        finally
        {
            AtomicWrite.Discard(temporary);
        }
    }
}
