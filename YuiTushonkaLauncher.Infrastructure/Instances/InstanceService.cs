using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Creates and manages instances: plans the hard-link layout, builds it, and keeps the database row in
/// sync with what is on disk.
/// </summary>
public sealed partial class InstanceService(
    IUnitOfWork unitOfWork,
    IFileLinkService fileLinks,
    BaseInstallProbe baseInstallProbe,
    PatchStateDetector patchStateDetector,
    ILogger<InstanceService> logger
) : IInstanceService
{
    /// <summary>Extra room reserved on top of the copied bytes, so a later patch cannot run out.</summary>
    private const long FreeSpaceHeadroomBytes = 256L * 1024 * 1024;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Instance>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
                (IReadOnlyList<Instance>)
                    await context
                        .Instances.AsNoTracking()
                        .Include(row => row.InstalledMods)
                        .OrderByDescending(row => row.CreatedAt)
                        .ToListAsync(token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<Instance?> GetAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .Instances.AsNoTracking()
                    .Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Files)
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public Task<InstancePlan> PlanAsync(InstanceCreationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => BuildPlan(request), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Instance> CreateAsync(
        InstanceCreationRequest request,
        IProgress<InstanceCreationProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(
            async () =>
            {
                var plan = BuildPlan(request);
                var instanceRoot = plan.InstancePath;
                var baseInstall = baseInstallProbe.Inspect(request.BaseInstallPath);

                progress?.Report(new InstanceCreationProgress("Creating directories", 0, plan.Entries.Count));

                var created = false;
                try
                {
                    Directory.CreateDirectory(instanceRoot);
                    created = true;

                    foreach (var directory in plan.Directories)
                    {
                        Directory.CreateDirectory(Path.Combine(instanceRoot, directory));
                    }

                    var completed = 0;
                    foreach (var entry in plan.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var source = Path.Combine(baseInstall.RootPath, entry.RelativePath);
                        var destination = Path.Combine(instanceRoot, entry.Destination);

                        switch (entry.Action)
                        {
                            case LinkAction.CreateDirectory:
                                Directory.CreateDirectory(destination);
                                break;
                            case LinkAction.Link:
                                fileLinks.CreateHardLink(destination, source);
                                break;
                            case LinkAction.Copy:
                                fileLinks.CopyFile(source, destination, overwrite: true);
                                break;
                            case LinkAction.Skip:
                                break;
                        }

                        completed++;
                        if (completed % 500 == 0)
                        {
                            progress?.Report(new InstanceCreationProgress("Building instance", completed, plan.Entries.Count));
                        }
                    }

                    progress?.Report(new InstanceCreationProgress("Reading patch state", plan.Entries.Count, plan.Entries.Count));
                    var patchState = patchStateDetector.Detect(instanceRoot);

                    var instance = new Instance
                    {
                        Id = Guid.NewGuid(),
                        Name = request.Name.Trim(),
                        Path = instanceRoot,
                        BaseGamePath = baseInstall.RootPath,
                        SptVersion = baseInstall.SptVersion ?? new SemanticVersion(0, 0, 0),
                        TarkovVersion = baseInstall.TarkovVersion ?? "unknown",
                        Layout = baseInstall.Layout,
                        ServerPort = 6969,
                        CreatedAt = DateTimeOffset.UtcNow,
                        Patch = new PatchInfo
                        {
                            IsPatched = patchState.State == PatchState.Patched,
                            OriginalSha256 = patchState.BackupSha256,
                            PatchedSha256 = patchState.TargetSha256,
                            BackupPath = patchState.BackupPath,
                            AppliedAt = patchState.State == PatchState.Patched ? DateTimeOffset.UtcNow : null,
                        },
                    };

                    await unitOfWork.ExecuteAsync(
                        async (context, token) =>
                        {
                            context.Instances.Add(instance);
                            context.ServerProfiles.Add(
                                new ServerProfile
                                {
                                    Id = Guid.NewGuid(),
                                    InstanceId = instance.Id,
                                    Port = instance.ServerPort,
                                    BackendUrl = $"https://127.0.0.1:{instance.ServerPort}",
                                    AutoSyncMods = false,
                                    UpdatedAt = DateTimeOffset.UtcNow,
                                }
                            );
                            await Task.CompletedTask;
                        },
                        cancellationToken
                    );

                    logger.LogInformation(
                        "Created instance {Name} at {Path}: {Links} linked, {Copies} copied, patch state {PatchState}",
                        instance.Name,
                        instance.Path,
                        plan.LinkCount,
                        plan.CopyCount,
                        patchState.State
                    );

                    return instance;
                }
                catch
                {
                    // Never leave a half-built instance behind: the next creation would collide with it.
                    if (created)
                    {
                        TryDeleteDirectory(instanceRoot);
                    }

                    throw;
                }
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid instanceId, bool deleteFiles, CancellationToken cancellationToken = default)
    {
        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) => await context.Instances.SingleOrDefaultAsync(row => row.Id == instanceId, token),
            cancellationToken
        );

        if (instance is null)
        {
            throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
        }

        if (deleteFiles)
        {
            TryDeleteDirectory(instance.Path);
        }

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.Instances.SingleAsync(entity => entity.Id == instanceId, token);
                context.Instances.Remove(row);
            },
            cancellationToken
        );

        logger.LogInformation("Deleted instance {Name} (files deleted: {DeleteFiles})", instance.Name, deleteFiles);
    }

    /// <inheritdoc />
    public async Task RenameAsync(Guid instanceId, string newName, CancellationToken cancellationToken = default)
    {
        ValidateName(newName);

        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) => await context.Instances.SingleOrDefaultAsync(row => row.Id == instanceId, token),
            cancellationToken
        );

        if (instance is null)
        {
            throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
        }

        var parent = Path.GetDirectoryName(instance.Path);
        if (string.IsNullOrEmpty(parent))
        {
            throw new InstanceValidationException([$"'{instance.Path}' has no parent directory."]);
        }

        var trimmed = newName.Trim();
        var target = Path.Combine(parent, trimmed);

        if (Directory.Exists(target))
        {
            throw new InstanceValidationException([$"'{target}' already exists."]);
        }

        Directory.Move(instance.Path, target);

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.Instances.SingleAsync(entity => entity.Id == instanceId, token);
                row.Name = trimmed;
                row.Path = target;
            },
            cancellationToken
        );

        logger.LogInformation("Renamed instance {Name} to {NewName} ({Path})", instance.Name, trimmed, target);
    }

    /// <inheritdoc />
    public async Task<PatchStateInfo> DetectPatchStateAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) => await context.Instances.SingleOrDefaultAsync(row => row.Id == instanceId, token),
            cancellationToken
        );

        if (instance is null)
        {
            throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
        }

        var state = patchStateDetector.Detect(instance.Path);

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.Instances.SingleAsync(entity => entity.Id == instanceId, token);
                row.Patch.IsPatched = state.State == PatchState.Patched;
                row.Patch.OriginalSha256 = state.BackupSha256;
                row.Patch.PatchedSha256 = state.TargetSha256;
                row.Patch.BackupPath = state.BackupPath;
            },
            cancellationToken
        );

        return state;
    }

    private InstancePlan BuildPlan(InstanceCreationRequest request)
    {
        ValidateName(request.Name);

        if (string.IsNullOrWhiteSpace(request.Location))
        {
            throw new InstanceValidationException(["No destination folder was selected."]);
        }

        var location = Path.GetFullPath(request.Location);
        if (!Directory.Exists(location))
        {
            throw new InstanceValidationException([$"The destination folder does not exist: {location}"]);
        }

        var baseInstall = baseInstallProbe.Inspect(request.BaseInstallPath);
        var instancePath = Path.Combine(location, request.Name.Trim());
        var issues = new List<string>();
        var warnings = new List<string>(baseInstall.Warnings);

        if (Directory.Exists(instancePath) || File.Exists(instancePath))
        {
            issues.Add($"'{instancePath}' already exists.");
        }

        if (!fileLinks.IsSameVolume(location, baseInstall.RootPath))
        {
            issues.Add(
                $"'{location}' is on a different volume than '{baseInstall.RootPath}'. Hard links cannot cross volumes; pick a folder on the same disk."
            );
        }
        else
        {
            TryProbeHardLinks(location, issues);
        }

        var entries = EnumerateEntries(baseInstall.RootPath, baseInstall.Shape);
        var copyBytes = entries.Where(entry => entry.Action == LinkAction.Copy).Sum(entry => entry.SizeBytes);
        var available = fileLinks.GetAvailableFreeSpace(location);
        if (available is { } free && free < copyBytes + FreeSpaceHeadroomBytes)
        {
            issues.Add(
                $"Not enough free space in '{location}': {copyBytes + FreeSpaceHeadroomBytes} bytes are needed but only {free} are available."
            );
        }

        if (issues.Count > 0)
        {
            throw new InstanceValidationException(issues);
        }

        return new InstancePlan
        {
            InstancePath = instancePath,
            Layout = baseInstall.Layout,
            Entries = entries,
            BaseShape = baseInstall.Shape,
            Directories = InstancePaths.RequiredDirectories(baseInstall.Layout),
            Warnings = warnings,
        };
    }

    private static List<InstancePlanEntry> EnumerateEntries(string baseInstallPath, InstallShape shape)
    {
        var entries = new List<InstancePlanEntry>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MatchType = MatchType.Simple,
        };

        foreach (var directory in Directory.EnumerateDirectories(baseInstallPath, "*", options))
        {
            var relative = Path.GetRelativePath(baseInstallPath, directory);
            var action = LinkPolicy.Decide(relative, isDirectory: true, shape);

            if (action is LinkAction.Skip)
            {
                continue;
            }

            entries.Add(new InstancePlanEntry(relative, action, 0, InstancePaths.FromBase(relative, shape)));
        }

        foreach (var file in Directory.EnumerateFiles(baseInstallPath, "*", options))
        {
            var relative = Path.GetRelativePath(baseInstallPath, file);
            var action = LinkPolicy.Decide(relative, isDirectory: false, shape);
            var destination = InstancePaths.FromBase(relative, shape);

            if (action is LinkAction.Skip)
            {
                // Still record it so the plan can report how much was deliberately left out.
                entries.Add(new InstancePlanEntry(relative, LinkAction.Skip, 0, destination));
                continue;
            }

            long size = 0;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // A file that vanishes mid-scan is simply not copied.
            }

            entries.Add(new InstancePlanEntry(relative, action, size, destination));
        }

        return entries;
    }

    private void TryProbeHardLinks(string location, List<string> issues)
    {
        var first = Path.Combine(location, $".ytt-probe-{Guid.NewGuid():n}.tmp");
        var second = Path.Combine(location, $".ytt-probe-{Guid.NewGuid():n}.tmp");

        try
        {
            File.WriteAllText(first, "probe");

            try
            {
                fileLinks.CreateHardLink(second, first);
                if (fileLinks.GetLinkCount(first) < 2)
                {
                    issues.Add($"'{location}' does not support hard links (link count did not increase).");
                }
            }
            catch (FileLinkException exception)
            {
                issues.Add($"'{location}' cannot hold instances: {exception.ToUserMessage()}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add($"'{location}' is not writable: {exception.Message}");
        }
        finally
        {
            TryDeleteFile(first);
            TryDeleteFile(second);
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !InstanceName().IsMatch(name.Trim()))
        {
            throw new InstanceValidationException([
                $"'{name}' is not a valid instance name: use 1-64 characters from letters, digits, '-' and '_'.",
            ]);
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove {Path}", path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover probe file is harmless.
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceName();
}
