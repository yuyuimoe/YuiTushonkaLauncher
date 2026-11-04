using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Mods;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Adopts a hand-made SPT installation: registers the folder as an instance and records the mods it
/// already contains, identified by GUID. Nothing is moved, linked or deleted — adoption is additive, so
/// the folder keeps working even if the user walks away.
/// </summary>
public sealed class MigrationService(
    IUnitOfWork unitOfWork,
    BaseInstallProbe probe,
    PatchStateDetector patchDetector,
    IModIdentityReader identityReader,
    IForgeClient forge,
    ILogger<MigrationService> logger
) : IMigrationService
{
    private const int GuidChunkSize = 40;

    /// <inheritdoc />
    public async Task<(Instance Instance, MigrationPlan Plan)> AdoptAsync(
        string path,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        var info = probe.Inspect(path);
        var trimmed = name.Trim();

        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var collision = await context
                    .Instances.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Name == trimmed || row.Path == info.RootPath, token);
                if (collision is not null)
                {
                    throw new InstanceValidationException([$"An instance named '{trimmed}' or at '{info.RootPath}' already exists."]);
                }

                var patch = patchDetector.Detect(info.RootPath);

                var row = new Instance
                {
                    Id = Guid.NewGuid(),
                    Name = trimmed,
                    Path = info.RootPath,
                    BaseGamePath = info.RootPath,
                    SptVersion = info.SptVersion ?? new SemanticVersion(0, 0, 0),
                    TarkovVersion = info.TarkovVersion ?? "unknown",
                    Layout = info.Layout,
                    ServerPort = 6969,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Patch = new PatchInfo
                    {
                        IsPatched = patch.State == PatchState.Patched,
                        OriginalSha256 = patch.BackupSha256,
                        PatchedSha256 = patch.TargetSha256,
                        BackupPath = patch.BackupPath,
                    },
                };

                context.Instances.Add(row);
                context.ServerProfiles.Add(
                    new ServerProfile
                    {
                        Id = Guid.NewGuid(),
                        InstanceId = row.Id,
                        Port = row.ServerPort,
                        BackendUrl = $"https://127.0.0.1:{row.ServerPort}",
                        AutoSyncMods = false,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    }
                );

                await Task.CompletedTask;
                return row;
            },
            cancellationToken
        );

        logger.LogInformation("Adopted {Path} as instance {Name}", instance.Path, instance.Name);

        var plan = await ScanAsync(instance.Id, cancellationToken);
        return (instance, plan);
    }

    /// <inheritdoc />
    public async Task<MigrationPlan> ScanAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context.Instances.AsNoTracking().SingleOrDefaultAsync(row => row.Id == instanceId, token)
                ?? throw new InstanceValidationException([$"Instance {instanceId} was not found."]),
            cancellationToken
        );

        var warnings = new List<string>();
        var candidates = EnumerateModFolders(instance, warnings);

        var guids = candidates
            .Where(candidate => candidate.Identity.HasGuid)
            .Select(candidate => candidate.Identity.Guid!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var catalogue = new Dictionary<string, ForgeMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in guids.Chunk(GuidChunkSize))
        {
            foreach (var mod in await forge.FindModsByGuidsAsync(chunk, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(mod.Guid))
                {
                    catalogue[mod.Guid] = mod;
                }
            }
        }

        // One entry per GUID: a "both halves" mod has a client folder and a server folder, and the plan
        // should describe the mod, not its folders.
        var entries = new List<MigrationEntry>(candidates.Count);
        var byGuid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var identity = candidate.Identity;
            ForgeMod? forgeMod = null;
            var matched = identity.Guid is not null && catalogue.TryGetValue(identity.Guid, out forgeMod);

            if (identity.Guid is not null && byGuid.TryGetValue(identity.Guid, out var index))
            {
                var existing = entries[index];
                entries[index] = existing with
                {
                    Files = [.. existing.Files, .. candidate.Files],
                    Kind = existing.Kind == identity.Kind ? existing.Kind : ModKind.Both,
                    IsDuplicate = true,
                };

                continue;
            }

            var entry = new MigrationEntry
            {
                Guid = identity.Guid,
                Name = forgeMod?.Name ?? identity.Name ?? Path.GetFileName(candidate.Folder),
                Version = identity.Version,
                Kind = identity.Kind,
                Match = identity.HasGuid
                    ? (matched ? MigrationMatch.Recognized : MigrationMatch.UnknownToForge)
                    : MigrationMatch.GuidMissing,
                ForgeId = forgeMod?.Id,
                Author = forgeMod?.Author,
                Files = candidate.Files,
            };

            entries.Add(entry);

            if (identity.Guid is not null)
            {
                byGuid[identity.Guid] = entries.Count - 1;
            }
        }

        var recognized = entries.Where(entry => entry.Match == MigrationMatch.Recognized).ToList();
        var unrecognized = entries.Where(entry => entry.Match == MigrationMatch.UnknownToForge).ToList();
        var withoutGuid = entries.Where(entry => entry.Match == MigrationMatch.GuidMissing).ToList();

        // A folder named after the GUID (where bare files were relocated before this phase) belongs to
        // the mod of that GUID; fold it back so the plan does not report a phantom mod.
        foreach (var guidless in entries.Where(entry => entry.Guid is null).ToList())
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(guidless.Files.FirstOrDefault() ?? string.Empty) ?? string.Empty);
            var owner = entries.FirstOrDefault(entry =>
                entry.Guid is not null && string.Equals(entry.Guid, folderName, StringComparison.OrdinalIgnoreCase)
            );

            if (owner is not null)
            {
                entries.Remove(guidless);
                var index = entries.IndexOf(owner);
                entries[index] = owner with { Files = [.. owner.Files, .. guidless.Files] };
            }
        }

        var owned = entries.SelectMany(entry => entry.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphanFiles = candidates.Count == 0 ? [] : EnumerateFilesUnderModRoots(instance).Where(file => !owned.Contains(file)).ToList();

        logger.LogInformation(
            "Migration scan of {Path}: {Recognized} recognized, {Unknown} unknown, {Missing} without GUID",
            instance.Path,
            recognized.Count,
            unrecognized.Count,
            withoutGuid.Count
        );

        return new MigrationPlan
        {
            InstanceId = instance.Id,
            InstancePath = instance.Path,
            Recognized = recognized,
            Unrecognized = unrecognized,
            WithoutGuid = withoutGuid,
            OrphanFiles = orphanFiles,
            Warnings = warnings,
        };
    }

    /// <inheritdoc />
    public async Task<MigrationResult> ApplyAsync(MigrationPlan plan, CancellationToken cancellationToken = default)
    {
        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context.Instances.AsNoTracking().SingleOrDefaultAsync(row => row.Id == plan.InstanceId, token)
                ?? throw new InstanceValidationException([$"Instance {plan.InstanceId} was not found."]),
            cancellationToken
        );

        var registered = 0;
        var skipped = 0;
        var hashed = 0;
        var warnings = new List<string>();

        foreach (var entry in plan.Recognized.Concat(plan.Unrecognized).Concat(plan.WithoutGuid))
        {
            var guid =
                entry.Guid
                ?? $"unrecognized.{InstancePathMapper.DefaultModFolder(Path.GetFileName(Path.GetDirectoryName(entry.Files.FirstOrDefault() ?? string.Empty) ?? "mod"))}";

            var files = new List<InstalledFile>(entry.Files.Count);
            foreach (var relative in entry.Files)
            {
                var absolute = Path.Combine(instance.Path, relative);
                if (!File.Exists(absolute))
                {
                    continue;
                }

                files.Add(
                    new InstalledFile
                    {
                        Id = Guid.NewGuid(),
                        RelativePath = relative.Replace('\\', '/'),
                        Sha256 = await HashFileAsync(absolute, cancellationToken),
                        OriginalSha256 = await HashFileAsync(absolute, cancellationToken),
                        Length = new FileInfo(absolute).Length,
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

            var inserted = await unitOfWork.ExecuteAsync(
                async (context, token) =>
                {
                    var exists = await context.InstalledMods.AnyAsync(
                        mod => mod.InstanceId == instance.Id && mod.Guid == guid && mod.ParentModGuid == null,
                        token
                    );

                    if (exists)
                    {
                        return false;
                    }

                    var row = new InstalledMod
                    {
                        Id = Guid.NewGuid(),
                        InstanceId = instance.Id,
                        Guid = guid,
                        Name = entry.Name,
                        Kind = entry.Kind,
                        Version = entry.Version ?? new SemanticVersion(0, 0, 0),
                        Recognition = entry.Match == MigrationMatch.Recognized ? ModRecognition.Recognized : ModRecognition.Unrecognized,
                        Source = InstalledModSource.Adopted,
                        InstalledAt = DateTimeOffset.UtcNow,
                    };

                    foreach (var file in files)
                    {
                        file.InstalledModId = row.Id;
                        row.Files.Add(file);
                    }

                    context.InstalledMods.Add(row);
                    await Task.CompletedTask;
                    return true;
                },
                cancellationToken
            );

            if (inserted)
            {
                registered++;
                hashed += files.Count;
            }
            else
            {
                skipped++;
            }
        }

        if (plan.OrphanFiles.Count > 0)
        {
            warnings.Add($"{plan.OrphanFiles.Count} file(s) under the mod roots belong to no identified mod and were left alone.");
        }

        logger.LogInformation(
            "Migration applied: {Registered} registered, {Skipped} skipped, {Hashed} files hashed",
            registered,
            skipped,
            hashed
        );

        return new MigrationResult
        {
            Registered = registered,
            Skipped = skipped,
            FilesHashed = hashed,
            Warnings = warnings,
        };
    }

    /// <summary>Every folder that looks like a mod, with the files it owns.</summary>
    private List<ModCandidate> EnumerateModFolders(Instance instance, List<string> warnings)
    {
        var candidates = new List<ModCandidate>();

        foreach (var (root, kind) in ModRoots(instance))
        {
            var absoluteRoot = Path.Combine(instance.Path, root);
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(absoluteRoot))
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, "spt", StringComparison.OrdinalIgnoreCase))
                {
                    // SPT's own plugins are not user mods.
                    continue;
                }

                var files = Directory
                    .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(instance.Path, file).Replace('\\', '/'))
                    .ToList();

                var assemblies = files.Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
                var identity = new ModIdentity { Kind = kind, Source = ModIdentitySource.None };

                foreach (var assembly in assemblies.Take(5))
                {
                    var read = identityReader.Read(Path.Combine(instance.Path, assembly), kind);
                    if (read.HasGuid)
                    {
                        identity = read;
                        break;
                    }
                }

                if (!identity.HasGuid)
                {
                    warnings.Add($"'{name}' has no readable mod GUID.");
                }

                candidates.Add(new ModCandidate(name, files, identity));
            }
        }

        return candidates;
    }

    private IEnumerable<string> EnumerateFilesUnderModRoots(Instance instance)
    {
        foreach (var (root, _) in ModRoots(instance))
        {
            var absoluteRoot = Path.Combine(instance.Path, root);
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(instance.Path, file).Replace('\\', '/');

                // SPT ships its own plugins in plugins/spt; they are not user mods and not orphans.
                if (relative.StartsWith("spt/BepInEx/plugins/spt/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return relative;
            }
        }
    }

    private static IEnumerable<(string Root, ModKind Kind)> ModRoots(Instance instance)
    {
        var runtime = LinkPolicy.RuntimeDirectory(instance.Layout);

        yield return ("spt/BepInEx/plugins", ModKind.Client);
        yield return ($"{runtime}/user/mods", ModKind.Server);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private sealed record ModCandidate(string Folder, IReadOnlyList<string> Files, ModIdentity Identity);
}
