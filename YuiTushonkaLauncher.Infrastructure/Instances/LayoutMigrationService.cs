using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Turns an instance built with the old split layout into SPT's merged one, and follows the move in the
/// database.
/// </summary>
/// <remarks>
/// Every entry is <em>renamed</em>, never copied: a rename keeps the inode, so the hard links a shared file has
/// with the base install (and with the other instances) are preserved and nothing is duplicated. The two
/// wrapper folders are removed only after their content is at the root.
/// </remarks>
public sealed class LayoutMigrationService(
    IUnitOfWork unitOfWork,
    IFileLinkService fileLinks,
    ISptServerSupervisor supervisor,
    ILogger<LayoutMigrationService> logger
) : ILayoutMigrationService
{
    private static readonly string[] Wrappers = ["gamefiles", "spt"];

    /// <inheritdoc />
    public async Task<IReadOnlyList<LayoutMigrationPlan>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var instances = await unitOfWork.ExecuteAsync(
            async (context, token) => await context.Instances.AsNoTracking().OrderBy(row => row.Name).ToListAsync(token),
            cancellationToken
        );

        var pending = new List<LayoutMigrationPlan>();

        foreach (var instance in instances)
        {
            var stored = await LoadStoredPathsAsync(instance.Id, cancellationToken);
            var plan = Plan(instance, stored);

            if (!plan.IsMigrated)
            {
                pending.Add(plan);
            }
        }

        return pending;
    }

    /// <inheritdoc />
    public async Task<LayoutMigrationPlan> PlanAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await LoadInstanceAsync(instanceId, cancellationToken);
        return Plan(instance, await LoadStoredPathsAsync(instanceId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<LayoutMigrationResult> MigrateAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await LoadInstanceAsync(instanceId, cancellationToken);
        var storedPaths = await LoadStoredPathsAsync(instanceId, cancellationToken);
        var plan = Plan(instance, storedPaths);

        if (plan.IsMigrated)
        {
            return new LayoutMigrationResult { Plan = plan };
        }

        if (plan.Issues.Count > 0)
        {
            throw new InstanceValidationException(plan.Issues);
        }

        var warnings = new List<string>();

        // Directories first, so a file can be renamed into a folder that did not exist at the root before.
        foreach (var directory in plan.Directories)
        {
            Directory.CreateDirectory(Path.Combine(instance.Path, directory));
        }

        var moved = 0;

        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = Path.Combine(instance.Path, entry.SourceRelativePath);
            var destination = Path.Combine(instance.Path, entry.DestinationRelativePath);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Move(source, destination);
                moved++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InstanceValidationException([
                    $"Could not move '{source}' to '{destination}': {exception.Message}. The instance is partly migrated; run again after fixing this file.",
                ]);
            }
        }

        var removed = RemoveWrapperFolders(instance.Path, warnings);
        var rewritten = await RewriteStoredPathsAsync(instance, cancellationToken);

        logger.LogInformation(
            "Migrated instance {Name} to the merged layout: {Moved} files moved, {Removed} folders removed, {Rewritten} stored paths rewritten",
            instance.Name,
            moved,
            removed,
            rewritten
        );

        return new LayoutMigrationResult
        {
            Plan = plan,
            FilesMoved = moved,
            DirectoriesRemoved = removed,
            StoredPathsRewritten = rewritten,
            Warnings = warnings,
        };
    }

    private LayoutMigrationPlan Plan(Instance instance, IReadOnlyList<(Guid ModId, string Path)> storedPaths)
    {
        var issues = new List<string>();
        var shape = InstancePaths.Detect(instance.Path);

        if (shape is null)
        {
            issues.Add($"'{instance.Path}' is neither a split nor a merged SPT folder, so it cannot be migrated.");
            return new LayoutMigrationPlan
            {
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                InstancePath = instance.Path,
                Shape = InstallShape.Split,
                Issues = issues,
            };
        }

        if (shape == InstallShape.Merged)
        {
            return new LayoutMigrationPlan
            {
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                InstancePath = instance.Path,
                Shape = InstallShape.Merged,
            };
        }

        if (supervisor.GetStatus(instance.Id).State is ServerState.Starting or ServerState.Running or ServerState.Stopping)
        {
            issues.Add($"The server of '{instance.Name}' is running; stop it before migrating the layout.");
        }

        var entries = new List<LayoutMigrationEntry>();
        var destinations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var wrapper in Wrappers)
        {
            var root = Path.Combine(instance.Path, wrapper);

            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(instance.Path, file).Replace('\\', '/');
                var destination = InstancePaths.FromBase(relative, InstallShape.Split);

                if (destination.Length == 0)
                {
                    continue;
                }

                if (!destinations.Add(destination))
                {
                    issues.Add($"Two files would land on '{destination}'; the folder cannot be merged without losing one.");
                    continue;
                }

                long size = 0;

                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // A file that vanishes mid-scan is reported by the move itself.
                }

                entries.Add(new LayoutMigrationEntry(relative, destination, size));
            }
        }

        return new LayoutMigrationPlan
        {
            InstanceId = instance.Id,
            InstanceName = instance.Name,
            InstancePath = instance.Path,
            Shape = shape.Value,
            Entries = entries,
            Directories = InstancePaths.RequiredDirectories(instance.Layout),
            StoredPathsToRewrite = storedPaths.Count(path => InstancePaths.IsLegacy(path.Path)),
            Issues = issues,
        };
    }

    /// <summary>
    /// Renames a file, keeping its inode and so its hard links. The one exception is a file that must be private to
    /// the instance: left shared it would be rewritten in every instance at once, so it is copied while moving.
    /// </summary>
    private void Move(string source, string destination)
    {
        var mustBePrivate =
            destination.Replace('\\', '/').EndsWith(InstancePaths.PatchedAssembly, StringComparison.OrdinalIgnoreCase)
            || destination.EndsWith(".spt-bak", StringComparison.OrdinalIgnoreCase);

        if (mustBePrivate && fileLinks.GetLinkCount(source) > 1)
        {
            var temporary = destination + ".ytt-move";
            fileLinks.CopyFile(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: false);

            // The copy replaces the shared name: leaving it behind would keep the old inode alive and leave the
            // wrapper folder occupied, which then cannot be removed.
            File.Delete(source);
            return;
        }

        File.Move(source, destination, overwrite: false);
    }

    private static int RemoveWrapperFolders(string instancePath, List<string> warnings)
    {
        var removed = 0;

        foreach (var wrapper in Wrappers)
        {
            var root = Path.Combine(instancePath, wrapper);

            if (!Directory.Exists(root))
            {
                continue;
            }

            // A file the plan could not see must stop the removal; empty folders are just leftovers of the move.
            if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any())
            {
                warnings.Add($"'{root}' still has files in it and was left in place; move them by hand or delete the folder.");
                continue;
            }

            Directory.Delete(root, recursive: true);
            removed++;
        }

        return removed;
    }

    private async Task<int> RewriteStoredPathsAsync(Instance instance, CancellationToken cancellationToken)
    {
        var rewritten = 0;

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instances = await context
                    .Instances.Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Files)
                    .FirstOrDefaultAsync(row => row.Id == instance.Id, token);

                if (instances is null)
                {
                    return;
                }

                foreach (var file in instances.InstalledMods.SelectMany(mod => mod.Files))
                {
                    if (!InstancePaths.IsLegacy(file.RelativePath))
                    {
                        continue;
                    }

                    file.RelativePath = InstancePaths.LegacyToMerged(file.RelativePath);
                    rewritten++;
                }

                if (
                    instances.Patch.BackupPath is { Length: > 0 } backup
                    && InstancePaths.IsLegacy(System.IO.Path.GetRelativePath(instance.Path, backup))
                )
                {
                    instances.Patch.BackupPath = Path.Combine(
                        instance.Path,
                        InstancePaths.LegacyToMerged(System.IO.Path.GetRelativePath(instance.Path, backup))
                    );
                }
            },
            cancellationToken
        );

        return rewritten;
    }

    private Task<Instance> LoadInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        return unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context.Instances.AsNoTracking().FirstOrDefaultAsync(row => row.Id == instanceId, token)
                ?? throw new InstanceValidationException([$"Instance {instanceId} was not found."]),
            cancellationToken
        );
    }

    private Task<IReadOnlyList<(Guid ModId, string Path)>> LoadStoredPathsAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        return unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var stored = await context
                    .InstalledFiles.AsNoTracking()
                    .Where(file => file.InstalledMod!.InstanceId == instanceId)
                    .Select(file => new { file.InstalledModId, file.RelativePath })
                    .ToListAsync(token);

                return (IReadOnlyList<(Guid, string)>)[.. stored.Select(row => (row.InstalledModId, row.RelativePath))];
            },
            cancellationToken
        );
    }
}
