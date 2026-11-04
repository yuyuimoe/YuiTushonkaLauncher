using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Installs addons, which attach to an installed mod (and are pinned to a <em>mod</em> version range, not
/// to an SPT version). An installed addon is stored as an <see cref="InstalledMod" /> with
/// <see cref="InstalledMod.ParentModGuid" /> set, so hashing, integrity and uninstall keep working.
/// </summary>
public sealed class AddonService(
    IUnitOfWork unitOfWork,
    IForgeDownloader downloader,
    IArchiveInspector inspector,
    IArchiveExtractor extractor,
    IModIdentityReader identityReader,
    ILinkAwareFileWriter writer,
    IAppPaths paths,
    ILogger<AddonService> logger
) : IAddonService
{
    /// <inheritdoc />
    public async Task<bool> CanInstallAsync(
        Guid instanceId,
        string parentModGuid,
        VersionRange constraint,
        SemanticVersion parentVersion,
        CancellationToken cancellationToken = default
    )
    {
        var parent = await FindInstalledAsync(instanceId, parentModGuid, cancellationToken);

        return parent is not null && constraint.Satisfies(parent.Version) && constraint.Satisfies(parentVersion);
    }

    /// <inheritdoc />
    public async Task<AddonInstallResult> InstallAsync(AddonInstallRequest request, CancellationToken cancellationToken = default)
    {
        var instance = await LoadInstanceAsync(request.InstanceId, cancellationToken);
        var warnings = new List<string>();

        var parent = instance.InstalledMods.FirstOrDefault(mod =>
            mod.ParentModGuid is null && string.Equals(mod.Guid, request.ParentModGuid, StringComparison.OrdinalIgnoreCase)
        );

        if (parent is null)
        {
            throw new InstanceValidationException([$"The parent mod '{request.ParentModGuid}' is not installed in this instance."]);
        }

        if (request.ModVersionConstraint is { } constraint && !constraint.Satisfies(parent.Version))
        {
            throw new InstanceValidationException([
                $"The addon supports mod versions '{constraint.Raw}', but '{parent.Name}' is {parent.Version}.",
            ]);
        }

        var archivePath = await ResolveArchiveAsync(request, cancellationToken);
        var inspection = inspector.Inspect(archivePath);

        if (inspection.IsUnsafe)
        {
            throw new InstanceValidationException([
                $"The addon archive contains entries that would escape the instance: {string.Join(", ", inspection.UnsafeEntries)}",
            ]);
        }

        var staging = Path.Combine(paths.CacheDirectory, "staging", Guid.NewGuid().ToString("n"));
        try
        {
            await extractor.ExtractAsync(archivePath, staging, cancellationToken);

            if (inspection.RootPrefix is { Length: > 0 } prefix)
            {
                var inner = Path.Combine(staging, prefix);
                if (Directory.Exists(inner))
                {
                    staging = inner;
                }
            }

            var identity = ReadIdentity(staging, inspection, warnings);
            var guid =
                identity.Guid
                ?? $"addon.{InstancePathMapper.DefaultModFolder(request.ParentModGuid)}.{InstancePathMapper.DefaultModFolder(Path.GetFileNameWithoutExtension(archivePath))}";
            var version = request.Version ?? identity.Version ?? new SemanticVersion(0, 0, 0);
            var parentFolder = ResolveParentFolder(instance.Path, parent, instance.Layout);

            var plan = PlanPlacement(staging, instance.Layout, parentFolder);
            var files = await PlaceFilesAsync(instance.Path, plan, cancellationToken);

            await SaveAsync(instance.Id, guid, request, identity, version, files, cancellationToken);

            return new AddonInstallResult
            {
                Guid = guid,
                ParentModGuid = request.ParentModGuid,
                Version = version,
                Files = [.. files.Select(file => file.RelativePath)],
                Warnings = warnings,
            };
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <inheritdoc />
    public async Task UninstallAsync(Guid instanceId, string addonGuid, CancellationToken cancellationToken = default)
    {
        var (instancePath, addon) = await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context
                    .InstalledMods.Include(mod => mod.Files)
                    .SingleOrDefaultAsync(mod => mod.InstanceId == instanceId && mod.Guid == addonGuid && mod.ParentModGuid != null, token);

                if (row is null)
                {
                    throw new InstanceValidationException([$"The addon '{addonGuid}' is not installed in this instance."]);
                }

                var instance = await context.Instances.AsNoTracking().SingleAsync(entity => entity.Id == instanceId, token);
                return (instance.Path, row);
            },
            cancellationToken
        );

        foreach (var file in addon.Files)
        {
            var path = Path.Combine(instancePath, file.RelativePath);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not remove {Path}", path);
            }
        }

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.InstalledMods.SingleAsync(entity => entity.Id == addon.Id, token);
                context.InstalledMods.Remove(row);
            },
            cancellationToken
        );
    }

    private async Task<InstalledMod?> FindInstalledAsync(Guid instanceId, string parentModGuid, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .InstalledMods.AsNoTracking()
                    .SingleOrDefaultAsync(
                        mod => mod.InstanceId == instanceId && mod.Guid == parentModGuid && mod.ParentModGuid == null,
                        token
                    ),
            cancellationToken
        );
    }

    private async Task<Instance> LoadInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .Instances.Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Files)
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token)
                ?? throw new InstanceValidationException([$"Instance {instanceId} was not found."]),
            cancellationToken
        );
    }

    private async Task<string> ResolveArchiveAsync(AddonInstallRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ArchivePath))
        {
            var path = Path.GetFullPath(request.ArchivePath);
            if (!File.Exists(path))
            {
                throw new InstanceValidationException([$"The addon archive '{path}' does not exist."]);
            }

            return path;
        }

        if (request.DownloadUrl is null)
        {
            throw new InstanceValidationException(["No addon archive and no download URL were provided."]);
        }

        var fileName = Path.GetFileName(Uri.UnescapeDataString(request.DownloadUrl.AbsolutePath));
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.') || fileName.StartsWith(".", StringComparison.Ordinal))
        {
            fileName = "addon.zip";
        }

        var result = await downloader.DownloadAsync(
            request.DownloadUrl,
            Path.Combine(paths.DownloadsDirectory, "addons"),
            fileName,
            progress: null,
            cancellationToken
        );

        return result.FilePath;
    }

    private ModIdentity ReadIdentity(string staging, ArchiveInspection inspection, List<string> warnings)
    {
        foreach (var candidate in inspection.CandidateAssemblies.Take(5))
        {
            var assemblyPath = Path.Combine(staging, candidate);
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            var identity = identityReader.Read(assemblyPath, inspection.Kind);
            if (identity.HasGuid)
            {
                return identity;
            }
        }

        warnings.Add("The addon did not declare its own GUID; a derived one was used.");
        return new ModIdentity { Kind = inspection.Kind, Source = ModIdentitySource.None };
    }

    /// <summary>The addon goes next to its parent: same folder when it is a client mod, the mod root otherwise.</summary>
    private static string ResolveParentFolder(string instancePath, InstalledMod parent, InstanceLayout layout)
    {
        // The assembly decides where the mod lives; a config file could sit under a different root.
        var assemblyFolder = parent
            .Files.Select(file => file.RelativePath)
            .FirstOrDefault(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && path.Contains('/'))
            is { } assembly
            ? Path.GetDirectoryName(assembly)?.Replace('\\', '/')
            : null;

        if (!string.IsNullOrEmpty(assemblyFolder) && !Path.IsPathRooted(assemblyFolder) && assemblyFolder.Contains('/'))
        {
            return assemblyFolder;
        }

        return InstancePathMapper.ModRoot(
            parent.Kind == ModKind.Server ? ModKind.Server : ModKind.Client,
            layout,
            InstancePathMapper.DefaultModFolder(parent.Guid)
        );
    }

    private static Dictionary<string, string> PlanPlacement(string staging, InstanceLayout layout, string parentFolder)
    {
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(staging, file).Replace('\\', '/');
            plan[file] = InstancePathMapper.IsLayoutPath(relative, layout)
                ? InstancePathMapper.Map(relative, layout)
                : $"{parentFolder}/{relative}";
        }

        return plan;
    }

    private async Task<List<InstalledFile>> PlaceFilesAsync(
        string instancePath,
        Dictionary<string, string> plan,
        CancellationToken cancellationToken
    )
    {
        var placed = new List<InstalledFile>(plan.Count);

        foreach (var pair in plan)
        {
            var relative = pair.Value.Replace('\\', '/');
            var destination = Path.Combine(instancePath, relative);

            await writer.CopyFileAsync(pair.Key, destination, overwrite: true, cancellationToken);

            var length = new FileInfo(destination).Length;
            var hash = await HashFileAsync(destination, cancellationToken);

            placed.Add(
                new InstalledFile
                {
                    Id = Guid.NewGuid(),
                    RelativePath = relative,
                    Sha256 = hash,
                    OriginalSha256 = hash,
                    Length = length,
                    IsConfig =
                        Path.GetExtension(relative).ToLowerInvariant()
                            is ".json"
                                or ".ini"
                                or ".cfg"
                                or ".toml"
                                or ".yaml"
                                or ".yml"
                                or ".xml",
                }
            );
        }

        return placed;
    }

    private async Task SaveAsync(
        Guid instanceId,
        string guid,
        AddonInstallRequest request,
        ModIdentity identity,
        SemanticVersion version,
        List<InstalledFile> files,
        CancellationToken cancellationToken
    )
    {
        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var existing = await context.InstalledMods.SingleOrDefaultAsync(
                    mod => mod.InstanceId == instanceId && mod.Guid == guid,
                    token
                );

                if (existing is not null)
                {
                    var stale = await context.InstalledMods.SingleAsync(mod => mod.Id == existing.Id, token);
                    context.InstalledMods.Remove(stale);
                }

                var row = new InstalledMod
                {
                    Id = Guid.NewGuid(),
                    InstanceId = instanceId,
                    Guid = guid,
                    Name = identity.Name ?? guid,
                    Kind = ModKind.Client,
                    Version = version,
                    ParentModGuid = request.ParentModGuid,
                    Recognition = identity.HasGuid ? ModRecognition.Recognized : ModRecognition.GuidMissing,
                    Source = request.DownloadUrl is null ? InstalledModSource.LocalArchive : InstalledModSource.Forge,
                    InstalledAt = DateTimeOffset.UtcNow,
                    ModId = await ResolveCatalogueRowAsync(context, guid, request, identity, token),
                };

                foreach (var file in files)
                {
                    file.InstalledModId = row.Id;
                    row.Files.Add(file);
                }

                context.InstalledMods.Add(row);
                await Task.CompletedTask;
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Links the addon to a catalogue row carrying its Forge id, so an export can name it and an import
    /// can fetch the exact version again. Without a Forge id there is nothing to record.
    /// </summary>
    private static async Task<Guid?> ResolveCatalogueRowAsync(
        LauncherDbContext context,
        string guid,
        AddonInstallRequest request,
        ModIdentity identity,
        CancellationToken cancellationToken
    )
    {
        if (request.ForgeId is not { } forgeId)
        {
            return null;
        }

        var row = await context.Mods.FirstOrDefaultAsync(mod => mod.ForgeId == forgeId, cancellationToken);
        if (row is not null)
        {
            return row.Id;
        }

        row = new Mod
        {
            Id = Guid.NewGuid(),
            ForgeId = forgeId,
            Guid = guid,
            Name = identity.Name ?? guid,
            Author = string.Empty,
            Kind = ModKind.Client,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        context.Mods.Add(row);

        return row.Id;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
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
            logger.LogDebug(exception, "Could not delete staging directory {Path}", path);
        }
    }
}
