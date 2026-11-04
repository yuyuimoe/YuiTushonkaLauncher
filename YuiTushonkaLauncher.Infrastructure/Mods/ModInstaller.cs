using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
/// Installs mods into an instance: download → inspect → identify → place files → record hashes.
/// </summary>
public sealed partial class ModInstaller(
    IUnitOfWork unitOfWork,
    IForgeDownloader downloader,
    IArchiveInspector inspector,
    IArchiveExtractor extractor,
    IModIdentityReader identityReader,
    ILinkAwareFileWriter writer,
    IAppPaths paths,
    ILogger<ModInstaller> logger
) : IModInstaller
{
    /// <inheritdoc />
    public async Task<ModInstallResult> InstallAsync(ModInstallRequest request, CancellationToken cancellationToken = default)
    {
        var instance = await LoadInstanceAsync(request.InstanceId, cancellationToken);
        var warnings = new List<string>();

        var archivePath = await ResolveArchiveAsync(request, cancellationToken);
        var inspection = inspector.Inspect(archivePath);

        if (inspection.IsUnsafe)
        {
            throw new InstanceValidationException([
                $"The archive '{Path.GetFileName(archivePath)}' contains entries that would escape the instance: {string.Join(", ", inspection.UnsafeEntries)}",
            ]);
        }

        var staging = Path.Combine(paths.CacheDirectory, "staging", Guid.NewGuid().ToString("n"));
        try
        {
            await extractor.ExtractAsync(archivePath, staging, cancellationToken);

            // A wrapped archive (MyMod-1.2.3/BepInEx/...) is inspected with its prefix stripped, so the
            // staging root has to be flattened to match.
            if (inspection.RootPrefix is { Length: > 0 } prefix)
            {
                var inner = Path.Combine(staging, prefix);
                if (Directory.Exists(inner))
                {
                    staging = inner;
                }
            }

            var identity = ReadIdentity(staging, inspection, warnings);
            var recognition = identity.HasGuid ? ModRecognition.Recognized : ModRecognition.GuidMissing;
            var guid = identity.Guid ?? BuildFallbackGuid(archivePath);
            var version = identity.Version ?? request.ExpectedVersion ?? new SemanticVersion(0, 0, 0);
            // The archive's own shape wins: a mod shipping both halves must stay "Both".
            var kind =
                inspection.Kind == ModKind.Both ? ModKind.Both
                : identity.Source == ModIdentitySource.ServerModMetadata ? ModKind.Server
                : inspection.Kind;

            if (recognition == ModRecognition.GuidMissing)
            {
                warnings.Add(
                    "No mod GUID could be read; the mod is installed but will not be updated or dependency-checked automatically."
                );
            }

            var existing = instance.InstalledMods.FirstOrDefault(mod => string.Equals(mod.Guid, guid, StringComparison.OrdinalIgnoreCase));
            var plan = PlanPlacement(staging, inspection, identity, kind, instance.Layout, guid);

            var sameFiles =
                existing is not null
                && existing
                    .Files.Select(file => file.RelativePath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .SequenceEqual(plan.Values.OrderBy(path => path, StringComparer.Ordinal));

            if (existing is not null && existing.Version == version && sameFiles)
            {
                return BuildResult(
                    existing,
                    recognition,
                    [.. existing.Files.Select(file => file.RelativePath)],
                    [.. existing.Files.Where(file => file.IsConfig).Select(file => file.RelativePath)],
                    ["The mod is already installed at this version."]
                );
            }

            if (existing is not null)
            {
                warnings.Add($"Replacing the installed version {existing.Version}.");
                RemoveFiles(instance.Path, existing);
            }

            var installedFiles = await PlaceFilesAsync(instance.Path, plan, cancellationToken);

            var installedMod = await SaveAsync(
                instance.Id,
                existing,
                guid,
                request,
                identity,
                kind,
                version,
                recognition,
                installedFiles,
                cancellationToken
            );

            return BuildResult(
                installedMod,
                recognition,
                [.. installedFiles.Select(file => file.RelativePath)],
                [.. installedFiles.Where(file => file.IsConfig).Select(file => file.RelativePath)],
                warnings
            );
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <inheritdoc />
    public async Task UninstallAsync(Guid instanceId, string modGuid, bool force = false, CancellationToken cancellationToken = default)
    {
        var (instancePath, mod, dependents) = await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instance = await context
                    .Instances.Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Files)
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token);

                if (instance is null)
                {
                    throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
                }

                var target = instance.InstalledMods.FirstOrDefault(item =>
                    string.Equals(item.Guid, modGuid, StringComparison.OrdinalIgnoreCase)
                );

                var requiring = await context
                    .Dependencies.Where(dependency => dependency.DependencyGuid == modGuid && dependency.Kind == DependencyKind.Required)
                    .Select(dependency => dependency.OwnerGuid)
                    .ToListAsync(token);

                var installedGuids = instance.InstalledMods.Select(item => item.Guid).ToList();
                var blockers = requiring.Where(installedGuids.Contains).ToList();

                return (instance.Path, target, blockers);
            },
            cancellationToken
        );

        if (mod is null)
        {
            throw new InstanceValidationException([$"The mod '{modGuid}' is not installed in this instance."]);
        }

        if (dependents.Count > 0 && !force)
        {
            throw new InstanceValidationException([
                $"'{modGuid}' is required by {string.Join(", ", dependents)}. Uninstall those first or force the removal.",
            ]);
        }

        RemoveFiles(instancePath, mod);

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.InstalledMods.SingleAsync(entity => entity.Id == mod.Id, token);
                context.InstalledMods.Remove(row);
            },
            cancellationToken
        );

        logger.LogInformation("Uninstalled {Guid} from instance {InstanceId}", modGuid, instanceId);
    }

    /// <inheritdoc />
    public async Task SetPinnedAsync(Guid instanceId, string modGuid, bool pinned, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.InstalledMods.SingleOrDefaultAsync(
                    entity => entity.InstanceId == instanceId && entity.Guid == modGuid,
                    token
                );

                if (row is null)
                {
                    throw new InstanceValidationException([$"The mod '{modGuid}' is not installed in this instance."]);
                }

                row.IsPinned = pinned;
            },
            cancellationToken
        );
    }

    private async Task<Instance> LoadInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instance = await context
                    .Instances.Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Files)
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token);

                return instance ?? throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
            },
            cancellationToken
        );
    }

    private async Task<string> ResolveArchiveAsync(ModInstallRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ArchivePath))
        {
            var path = Path.GetFullPath(request.ArchivePath);
            if (!File.Exists(path))
            {
                throw new InstanceValidationException([$"The archive '{path}' does not exist."]);
            }

            return path;
        }

        if (request.DownloadUrl is null)
        {
            throw new InstanceValidationException(["No archive and no download URL were provided."]);
        }

        var fileName = Path.GetFileName(Uri.UnescapeDataString(request.DownloadUrl.AbsolutePath));
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.'))
        {
            fileName = "mod.zip";
        }

        var downloadDirectory = Path.Combine(paths.DownloadsDirectory, "mods");
        var result = await downloader.DownloadAsync(request.DownloadUrl, downloadDirectory, fileName, progress: null, cancellationToken);

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

        warnings.Add("None of the assemblies in the archive declared a mod GUID.");
        return new ModIdentity
        {
            Kind = inspection.Kind,
            Source = ModIdentitySource.None,
            Reason = "No readable mod identity.",
        };
    }

    private Dictionary<string, string> PlanPlacement(
        string staging,
        ArchiveInspection inspection,
        ModIdentity identity,
        ModKind kind,
        InstanceLayout layout,
        string guid
    )
    {
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToList();

        // First pass: everything the archive already roots at the instance layout.
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(staging, file).Replace('\\', '/');
            if (InstancePathMapper.IsLayoutPath(relative, layout))
            {
                plan[file] = InstancePathMapper.Map(relative, layout);
            }
        }

        // The anchor is the folder holding the first placed assembly: bare files (a README, a helper
        // executable) belong with it, not in a separate folder named after the GUID.
        var anchor = plan.Where(pair => pair.Value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(pair => Path.GetDirectoryName(pair.Value)?.Replace('\\', '/'))
            .FirstOrDefault(folder => !string.IsNullOrEmpty(folder));

        anchor ??=
            kind == ModKind.Server
                ? InstancePathMapper.ModRoot(ModKind.Server, layout, InstancePathMapper.DefaultModFolder(guid))
                : InstancePathMapper.ModRoot(ModKind.Client, layout, InstancePathMapper.DefaultModFolder(guid));

        foreach (var file in files)
        {
            if (plan.ContainsKey(file))
            {
                continue;
            }

            var relative = Path.GetRelativePath(staging, file).Replace('\\', '/');
            plan[file] = $"{anchor}/{relative}";
        }

        if (identity.Source == ModIdentitySource.BepInPluginAttribute && kind == ModKind.Server)
        {
            logger.LogWarning("Identity came from a plugin attribute but the archive looks server-side; using {Anchor}", anchor);
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
            var source = pair.Key;
            var relative = pair.Value.Replace('\\', '/');
            var destination = Path.Combine(instancePath, relative);

            await writer.CopyFileAsync(source, destination, overwrite: true, cancellationToken);

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
                    IsConfig = IsConfigFile(relative),
                }
            );
        }

        return placed;
    }

    private async Task<InstalledMod> SaveAsync(
        Guid instanceId,
        InstalledMod? existing,
        string guid,
        ModInstallRequest request,
        ModIdentity identity,
        ModKind kind,
        SemanticVersion version,
        ModRecognition recognition,
        List<InstalledFile> files,
        CancellationToken cancellationToken
    )
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                // Replacing an installation deletes the old row (its files cascade) and inserts a
                // fresh one: mutating a tracked collection across contexts only invites drift.
                if (existing is not null)
                {
                    var stale = await context.InstalledMods.SingleOrDefaultAsync(mod => mod.Id == existing.Id, token);
                    if (stale is not null)
                    {
                        context.InstalledMods.Remove(stale);
                    }
                }

                var row = new InstalledMod
                {
                    Id = Guid.NewGuid(),
                    InstanceId = instanceId,
                    Guid = guid,
                };
                context.InstalledMods.Add(row);

                row.ModId = await ResolveModIdAsync(context, guid, request, token);
                // The last URL segment of a Forge download is the version ("1.2.1"), which is not a name; the GUID is.
                row.Name = identity.Name ?? guid;
                row.Kind = kind;
                row.Version = version;
                row.IsPinned = request.Pin;
                row.Recognition = recognition;
                row.Source = request.DownloadUrl is null ? InstalledModSource.LocalArchive : InstalledModSource.Forge;
                row.InstalledAt = DateTimeOffset.UtcNow;

                foreach (var file in files)
                {
                    file.InstalledModId = row.Id;
                    row.Files.Add(file);
                }

                await Task.CompletedTask;
                return row;
            },
            cancellationToken
        );
    }

    private static async Task<Guid?> ResolveModIdAsync(
        LauncherDbContext context,
        string guid,
        ModInstallRequest request,
        CancellationToken cancellationToken
    )
    {
        var catalogue = await context.Mods.SingleOrDefaultAsync(mod => mod.Guid == guid, cancellationToken);
        if (catalogue is not null)
        {
            return catalogue.Id;
        }

        if (request.ForgeId is null)
        {
            return null;
        }

        var created = new Mod
        {
            Id = Guid.NewGuid(),
            ForgeId = request.ForgeId,
            Guid = guid,
            Name = guid,
            Author = string.Empty,
            Kind = ModKind.Client,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        context.Mods.Add(created);
        return created.Id;
    }

    private void RemoveFiles(string instancePath, InstalledMod mod)
    {
        foreach (var file in mod.Files)
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

        // Drop directories that are now empty, deepest first, but never the layout roots themselves.
        foreach (
            var directory in mod
                .Files.Select(file => Path.GetDirectoryName(file.RelativePath))
                .Where(dir => !string.IsNullOrEmpty(dir))
                .Distinct()
        )
        {
            var full = Path.Combine(instancePath, directory!);
            try
            {
                if (Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any())
                {
                    Directory.Delete(full);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(exception, "Could not remove directory {Path}", full);
            }
        }
    }

    private static bool IsConfigFile(string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".json" or ".ini" or ".cfg" or ".toml" or ".yaml" or ".yml" or ".xml" => true,
            _ => false,
        };
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string BuildFallbackGuid(string archivePath)
    {
        return $"unrecognized.{Sanitize(Path.GetFileNameWithoutExtension(archivePath))}";
    }

    private static string Sanitize(string value)
    {
        var cleaned = UnsupportedChars().Replace(value.ToLowerInvariant(), "-").Trim('-', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? "mod" : cleaned[..Math.Min(cleaned.Length, 48)];
    }

    private static ModInstallResult BuildResult(
        InstalledMod mod,
        ModRecognition recognition,
        IReadOnlyList<string> files,
        IReadOnlyList<string> configs,
        IReadOnlyList<string> warnings
    )
    {
        return new ModInstallResult
        {
            InstalledModId = mod.Id,
            Guid = mod.Guid,
            Name = mod.Name,
            Kind = mod.Kind,
            Version = mod.Version,
            Recognition = recognition,
            Files = files,
            ConfigFiles = configs,
            Warnings = warnings,
        };
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

    [GeneratedRegex("[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedChars();
}
