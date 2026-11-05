using Microsoft.EntityFrameworkCore;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Logs;

/// <summary>
/// Finds what a player needs when something breaks, from the four places SPT writes: the game's per-run folder, the
/// BepInEx log, the server's daily file, and the launcher's own file. Measured paths, not guesses.
/// </summary>
public sealed class LogCatalog(IUnitOfWork unitOfWork, IAppPaths paths) : ILogCatalog
{
    /// <summary>The game's run folders to look at, newest first.</summary>
    private const int MaxRuns = 3;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LogFile>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) => await context.Instances.AsNoTracking().FirstOrDefaultAsync(row => row.Id == instanceId, token),
            cancellationToken
        );

        if (instance is null)
        {
            return [];
        }

        var files = new List<LogFile>();

        files.AddRange(LauncherFiles());
        files.AddRange(GameFiles(instance));
        files.AddRange(Optional(Path.Combine(instance.Path, "BepInEx", "LogOutput.log"), LogSourceKind.BepInEx, isDefault: true));
        files.AddRange(ServerFiles(instance));

        return [.. files.OrderByDescending(file => file.LastWrite ?? DateTimeOffset.MinValue)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LogFile>> ListAsync(Guid instanceId, LogSourceKind kind, CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(instanceId, cancellationToken);
        return [.. all.Where(file => file.Kind == kind)];
    }

    private IEnumerable<LogFile> LauncherFiles()
    {
        foreach (var file in Directory.Exists(paths.LogsDirectory) ? Directory.EnumerateFiles(paths.LogsDirectory, "*.log*") : [])
        {
            if (Describe(LogSourceKind.Launcher, file, isDefault: true) is { } described)
            {
                yield return described;
            }
        }
    }

    private static IEnumerable<LogFile> GameFiles(Instance instance)
    {
        var root = Path.Combine(instance.Path, InstancePaths.GameLogs);

        if (!Directory.Exists(root))
        {
            yield break;
        }

        var runs = Directory
            .EnumerateDirectories(root)
            .Select(directory => new { Directory = directory, Modified = Directory.GetLastWriteTimeUtc(directory) })
            .OrderByDescending(run => run.Modified)
            .Take(MaxRuns);

        foreach (var run in runs)
        {
            foreach (var file in Directory.EnumerateFiles(run.Directory, "*.log").OrderBy(name => name, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file).ToLowerInvariant();

                // The trace and error logs are the ones a session fills with hundreds of megabytes: available, not default.
                var isDefault =
                    name.Contains("application", StringComparison.Ordinal) || name.Contains("assetbundle", StringComparison.Ordinal);

                if (Describe(LogSourceKind.Game, file, isDefault) is { } described)
                {
                    yield return described;
                }
            }
        }
    }

    private static IEnumerable<LogFile> ServerFiles(Instance instance)
    {
        var directory = Path.Combine(instance.Path, InstancePaths.UserDirectory(instance.Layout), "logs", "spt");

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        // One file per day: the newest few are what a report needs.
        foreach (var file in Directory.EnumerateFiles(directory, "*.log").OrderByDescending(name => name, StringComparer.Ordinal).Take(5))
        {
            if (Describe(LogSourceKind.Server, file, isDefault: true) is { } described)
            {
                yield return described;
            }
        }
    }

    private static IEnumerable<LogFile> Optional(string path, LogSourceKind kind, bool isDefault)
    {
        if (Describe(kind, path, isDefault) is { } described)
        {
            yield return described;
        }
    }

    private static LogFile? Describe(LogSourceKind kind, string path, bool isDefault)
    {
        try
        {
            var info = new FileInfo(path);

            return info.Exists
                ? new LogFile(
                    kind,
                    info.FullName,
                    info.Name,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                    isDefault
                )
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
