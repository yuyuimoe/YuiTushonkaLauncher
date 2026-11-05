using System.IO.Compression;
using YuiTushonkaLauncher.Core.Logs;

namespace YuiTushonkaLauncher.Infrastructure.Logs;

/// <summary>
/// Rotation by count and age, with the previous files gzipped into <c>archive/</c> ([D17]). Rotation itself is
/// Serilog's (it rolls the active file); this decides what is kept afterwards.
/// </summary>
public sealed class LogRetention(TimeProvider time) : ILogRetention
{
    /// <summary>The folder compressed files go to, next to the logs.</summary>
    public const string ArchiveDirectory = "archive";

    /// <inheritdoc />
    public RetentionPlan Plan(string logsDirectory, int keepFiles, int keepDays)
    {
        if (!Directory.Exists(logsDirectory))
        {
            return new RetentionPlan();
        }

        var now = time.GetUtcNow();
        var files = Directory
            .EnumerateFiles(logsDirectory, "*.log*")
            .Where(file => !IsActive(file))
            .Select(file => new RotatedLog(
                file,
                new FileInfo(file).Length,
                new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero)
            ))
            .OrderByDescending(file => file.Modified)
            .ToList();

        // A file that is not compressed yet is compressed, not deleted: the reader can still open a .gz.
        var toCompress = files.Where(file => !IsCompressed(file.Path)).Select(file => file.Path).ToList();
        var uncompressed = files.Where(file => !IsCompressed(file.Path)).ToList();
        var compressed = files.Where(file => IsCompressed(file.Path)).ToList();

        var tooOld = files.Where(file => keepDays > 0 && (now - file.Modified).TotalDays > keepDays).ToList();
        var toDelete = tooOld.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);

        // Count is applied after age, on what survives it, newest first.
        var survivors = files.Where(file => !toDelete.Contains(file.Path)).ToList();

        foreach (var file in survivors.Skip(Math.Max(keepFiles, 1)))
        {
            toDelete.Add(file.Path);
        }

        return new RetentionPlan
        {
            ToCompress = toCompress.Where(path => !toDelete.Contains(path)).ToList(),
            ToDelete = [.. toDelete],
            BytesFreed = files.Where(file => toDelete.Contains(file.Path)).Sum(file => file.Bytes),
        };
    }

    /// <inheritdoc />
    public async Task<RetentionPlan> ApplyAsync(
        string logsDirectory,
        int keepFiles,
        int keepDays,
        CancellationToken cancellationToken = default
    )
    {
        var plan = Plan(logsDirectory, keepFiles, keepDays);

        foreach (var path in plan.ToCompress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var archive = Path.Combine(logsDirectory, ArchiveDirectory);
            Directory.CreateDirectory(archive);
            var target = Path.Combine(archive, Path.GetFileName(path) + ".gz");
            var temporary = target + ".tmp";

            await using (var source = File.OpenRead(path))
            await using (var destination = File.Create(temporary))
            await using (var gzip = new GZipStream(destination, CompressionLevel.SmallestSize))
            {
                await source.CopyToAsync(gzip, cancellationToken);
            }

            File.Move(temporary, target, overwrite: true);
            File.Delete(path);
        }

        foreach (var path in plan.ToDelete)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file held open by another process is tried again on the next pass.
            }
        }

        return plan;
    }

    /// <summary>A file the launcher is writing right now must never be compressed or deleted.</summary>
    private bool IsActive(string path)
    {
        return (time.GetUtcNow() - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)).TotalMinutes < 5;
    }

    private static bool IsCompressed(string path)
    {
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
    }
}
