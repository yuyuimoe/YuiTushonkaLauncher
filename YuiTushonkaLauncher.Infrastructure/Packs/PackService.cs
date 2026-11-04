using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Patching;

namespace YuiTushonkaLauncher.Infrastructure.Packs;

/// <summary>
/// Exports an instance's composition and rebuilds it from a package.
/// </summary>
/// <remarks>
/// <para>
/// A package is a list of names and exact versions plus the config files the user edited. It never
/// carries a binary and never supplies a URL: the import asks the Forge for the exact version and takes
/// the download address from the Forge's answer. Config files are written only into files that a mod of
/// the package owns, so a package cannot create arbitrary files either.
/// </para>
/// <para>
/// Applying a config updates the file's baseline hash in the database, so the integrity check treats the
/// imported content as the accepted state (and a later re-export reproduces the same package) while
/// <see cref="InstalledFile.OriginalSha256" /> keeps what the mod shipped.
/// </para>
/// </remarks>
public sealed class PackService(
    IUnitOfWork unitOfWork,
    IForgeClient forge,
    IModInstaller installer,
    IAddonService addons,
    ILinkAwareFileWriter writer,
    PackReader reader,
    ILogger<PackService> logger,
    TimeProvider? timeProvider = null
) : IPackService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<ExportResult> ExportAsync(
        Guid instanceId,
        string packagePath,
        ExportOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new ExportOptions();
        var instance = await LoadInstanceAsync(instanceId, cancellationToken);

        var skipped = new List<SkippedFile>();
        var warnings = new List<string>();
        var configContent = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var packConfigs = new List<PackConfig>();
        long total = 0;

        if (options.IncludeConfigs)
        {
            foreach (var owner in instance.InstalledMods.OrderBy(mod => mod.Guid, StringComparer.Ordinal))
            {
                foreach (var file in owner.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
                {
                    var candidate = await TryCollectConfigAsync(instance, owner, file, options, skipped, total, cancellationToken);
                    if (candidate is null || configContent.ContainsKey(candidate.Config.Path))
                    {
                        continue;
                    }

                    total += candidate.Content.Length;
                    configContent[candidate.Config.Path] = candidate.Content;
                    packConfigs.Add(candidate.Config);
                }
            }
        }

        var mods = instance
            .InstalledMods.Where(mod => !mod.IsAddon)
            .OrderBy(mod => mod.Guid, StringComparer.Ordinal)
            .Select(mod => new PackMod
            {
                Guid = mod.Guid,
                ForgeId = mod.Mod?.ForgeId,
                Name = mod.Name,
                Version = mod.Version.ToString(),
                Kind = mod.Kind,
                Pinned = mod.IsPinned,
                ContentSha256 = ContentDigest(mod),
            })
            .ToList();

        var packAddons = instance
            .InstalledMods.Where(mod => mod.IsAddon)
            .OrderBy(mod => mod.ParentModGuid, StringComparer.Ordinal)
            .ThenBy(mod => mod.Guid, StringComparer.Ordinal)
            .Select(mod => new PackAddon
            {
                ParentGuid = mod.ParentModGuid!,
                Guid = mod.Guid,
                ForgeId = mod.Mod?.ForgeId,
                Name = mod.Name,
                Version = mod.Version.ToString(),
                ContentSha256 = ContentDigest(mod),
            })
            .ToList();

        warnings.AddRange(
            mods.Where(mod => mod.ForgeId is null)
                .Select(mod => $"'{mod.Name}' did not come from the Forge: whoever imports this package must install it by hand.")
        );
        warnings.AddRange(
            packAddons
                .Where(addon => addon.ForgeId is null)
                .Select(addon => $"The addon '{addon.Name}' did not come from the Forge: it cannot be downloaded on import.")
        );

        var manifest = new PackManifest
        {
            FormatVersion = PackFormat.CurrentVersion,
            CreatedAt = _time.GetUtcNow(),
            AppVersion = AppVersion(),
            InstanceName = instance.Name,
            SptVersion = instance.SptVersion.ToString(),
            TarkovVersion = instance.TarkovVersion,
            Layout = instance.Layout,
            Mods = mods,
            Addons = packAddons,
            Configs = packConfigs.OrderBy(config => config.Path, StringComparer.Ordinal).ToList(),
        };

        manifest = manifest with { ShareCode = PackManifestSerializer.ComputeShareCode(manifest) };
        PackWriter.Write(packagePath, manifest, configContent);

        logger.LogInformation(
            "Exported {Instance} to {Package}: {Mods} mod(s), {Addons} addon(s), {Configs} config(s), {Skipped} skipped, share code {Code}",
            instance.Name,
            packagePath,
            mods.Count,
            packAddons.Count,
            packConfigs.Count,
            skipped.Count,
            manifest.ShareCode
        );

        return new ExportResult
        {
            PackagePath = packagePath,
            ShareCode = manifest.ShareCode,
            Manifest = manifest,
            Skipped = skipped,
            Warnings = warnings,
        };
    }

    /// <inheritdoc />
    public async Task<ImportPlan> PlanImportAsync(Guid instanceId, string packagePath, CancellationToken cancellationToken = default)
    {
        var contents = reader.Read(packagePath);
        var instance = await LoadInstanceAsync(instanceId, cancellationToken);

        return await BuildPlanAsync(contents, instance, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(
        Guid instanceId,
        string packagePath,
        ImportOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new ImportOptions();
        var contents = reader.Read(packagePath);
        var instance = await LoadInstanceAsync(instanceId, cancellationToken);
        var plan = await BuildPlanAsync(contents, instance, cancellationToken);

        var results = new List<ImportItemResult>();
        var warnings = new List<string>(plan.Warnings);
        var compareContent = contents.Manifest.Layout == instance.Layout;

        if (!compareContent)
        {
            warnings.Add("The package was made for another folder layout: installed content cannot be compared with the exporter's.");
        }

        foreach (var item in plan.Items.Where(item => item.Kind == ImportItemKind.Mod))
        {
            var packMod = contents.Manifest.Mods.First(mod => string.Equals(mod.Guid, item.Id, StringComparison.OrdinalIgnoreCase));
            results.Add(await ApplyModAsync(instance, item, packMod, options, compareContent, progress, warnings, cancellationToken));
        }

        foreach (var item in plan.Items.Where(item => item.Kind == ImportItemKind.Addon))
        {
            var packAddon = contents.Manifest.Addons.First(addon => string.Equals(addon.Guid, item.Id, StringComparison.OrdinalIgnoreCase));
            results.Add(await ApplyAddonAsync(instance, item, packAddon, compareContent, progress, cancellationToken));
        }

        foreach (var item in plan.Items.Where(item => item.Kind == ImportItemKind.Config))
        {
            var config = contents.Manifest.Configs.First(entry => entry.Path == item.Id);
            results.Add(
                await ApplyConfigAsync(instance, item, config, contents.Configs[config.Path], options, progress, cancellationToken)
            );
        }

        var result = new ImportResult { Items = results, Warnings = warnings };
        logger.LogInformation(
            "Imported {Package} into {Instance}: {Summary}",
            Path.GetFileName(packagePath),
            instance.Name,
            result.Summary
        );

        return result;
    }

    /// <summary>
    /// Decides whether one tracked file of a mod belongs in the package. The installer's <c>IsConfig</c> flag is
    /// deliberately not consulted: it misses types a user does edit (a <c>.txt</c> message of the day), and a
    /// file that was edited but cannot be shared must be reported, not silently dropped.
    /// </summary>
    private async Task<ConfigCandidate?> TryCollectConfigAsync(
        Instance instance,
        InstalledMod owner,
        InstalledFile file,
        ExportOptions options,
        List<SkippedFile> skipped,
        long totalSoFar,
        CancellationToken cancellationToken
    )
    {
        var path = file.RelativePath.Replace('\\', '/');
        var full = Path.Combine(instance.Path, path);

        if (!File.Exists(full))
        {
            return null;
        }

        var current = FileHash.TryCompute(full);
        if (current is null)
        {
            skipped.Add(new SkippedFile(path, "The file could not be read."));

            return null;
        }

        // Untouched since the mod shipped it: nothing to share.
        if (string.Equals(current, file.OriginalSha256 ?? file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (options.ExcludedConfigs.Contains(path, StringComparer.Ordinal))
        {
            skipped.Add(new SkippedFile(path, "Left out at your request."));

            return null;
        }

        var reason = RefusalReason(path, new FileInfo(full).Length, totalSoFar);
        if (reason is not null)
        {
            skipped.Add(new SkippedFile(path, reason));

            return null;
        }

        var content = await File.ReadAllBytesAsync(full, cancellationToken);

        return new ConfigCandidate(
            new PackConfig
            {
                ModGuid = owner.Guid,
                Path = path,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                OriginalSha256 = file.OriginalSha256,
            },
            content
        );
    }

    private static string? RefusalReason(string path, long length, long totalSoFar)
    {
        if (!PackRules.IsSafeRelativePath(path))
        {
            return "The path is not a safe instance-relative path.";
        }

        if (PackRules.IsForbiddenExtension(path) || !PackRules.IsAllowedConfigExtension(path))
        {
            return $"'{Path.GetExtension(path)}' files are not shareable.";
        }

        if (length > PackRules.MaxConfigBytes)
        {
            return $"The file is {length / 1024:N0} KiB; the limit is {PackRules.MaxConfigBytes / 1024:N0} KiB.";
        }

        return totalSoFar + length > PackRules.MaxTotalBytes ? "The package would grow past its size limit." : null;
    }

    private async Task<ImportPlan> BuildPlanAsync(PackContents contents, Instance instance, CancellationToken cancellationToken)
    {
        var manifest = contents.Manifest;
        var warnings = new List<string>();
        var items = new List<ImportPlanItem>();

        if (
            SemanticVersion.TryParse(manifest.SptVersion, out var packSpt)
            && (packSpt.Major != instance.SptVersion.Major || packSpt.Minor != instance.SptVersion.Minor)
        )
        {
            warnings.Add($"The package was made for SPT {manifest.SptVersion}, but this instance runs SPT {instance.SptVersion}.");
        }

        if (manifest.Layout != instance.Layout)
        {
            warnings.Add("The package was made for another folder layout: config files may not match.");
        }

        var installed = instance.InstalledMods.ToDictionary(mod => mod.Guid, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in manifest.Mods)
        {
            items.Add(await PlanModAsync(mod, installed, instance.SptVersion, cancellationToken));
        }

        foreach (var addon in manifest.Addons)
        {
            items.Add(await PlanAddonAsync(addon, manifest, items, installed, cancellationToken));
        }

        foreach (var config in manifest.Configs)
        {
            items.Add(PlanConfig(config, contents.Configs[config.Path], instance, installed, items));
        }

        return new ImportPlan
        {
            Manifest = manifest,
            Items = items,
            Warnings = warnings,
        };
    }

    private async Task<ImportPlanItem> PlanModAsync(
        PackMod mod,
        Dictionary<string, InstalledMod> installed,
        SemanticVersion instanceSpt,
        CancellationToken cancellationToken
    )
    {
        ImportPlanItem Item(ImportAction action, string reason, string? url = null)
        {
            return new ImportPlanItem
            {
                Kind = ImportItemKind.Mod,
                Id = mod.Guid,
                Name = mod.Name,
                Version = mod.Version,
                Action = action,
                Reason = reason,
                DownloadUrl = url,
                ForgeId = mod.ForgeId,
            };
        }

        installed.TryGetValue(mod.Guid, out var existing);
        var wanted = SemanticVersion.Parse(mod.Version);

        if (existing is { IsAddon: false } && existing.Version == wanted)
        {
            return Item(ImportAction.AlreadyInstalled, $"Version {mod.Version} is already installed.");
        }

        if (mod.ForgeId is not { } forgeId)
        {
            return Item(ImportAction.Unavailable, "It did not come from the Forge, so it cannot be downloaded: install it by hand.");
        }

        IReadOnlyList<ForgeModVersion> versions;
        try
        {
            versions = await forge.GetModVersionsAsync(forgeId, cancellationToken);
        }
        catch (ForgeException exception)
        {
            return Item(ImportAction.Unavailable, $"The Forge could not be reached: {exception.Message}");
        }

        var match = PickVersion(versions, mod.Version, wanted);
        if (match is null)
        {
            return Item(ImportAction.Unavailable, $"Version {mod.Version} is no longer on the Forge.");
        }

        if (!IsHttps(match.DownloadUrl))
        {
            return Item(ImportAction.Unavailable, "The Forge returned a download address that is not https; it was not trusted.");
        }

        var compatibility = match.SptConstraint.Satisfies(instanceSpt)
            ? string.Empty
            : $" It declares SPT '{match.SptConstraint.Raw}', but this instance runs {instanceSpt}.";

        return existing is null
            ? Item(ImportAction.Install, $"Download and install {mod.Version}.{compatibility}", match.DownloadUrl)
            : Item(
                ImportAction.Replace,
                $"Version {existing.Version} is installed; {mod.Version} would replace it.{compatibility}",
                match.DownloadUrl
            );
    }

    private async Task<ImportPlanItem> PlanAddonAsync(
        PackAddon addon,
        PackManifest manifest,
        List<ImportPlanItem> items,
        Dictionary<string, InstalledMod> installed,
        CancellationToken cancellationToken
    )
    {
        ImportPlanItem Item(ImportAction action, string reason, string? url = null, string? constraint = null)
        {
            return new ImportPlanItem
            {
                Kind = ImportItemKind.Addon,
                Id = addon.Guid,
                Name = addon.Name,
                Version = addon.Version,
                Action = action,
                Reason = reason,
                DownloadUrl = url,
                ForgeId = addon.ForgeId,
                ParentGuid = addon.ParentGuid,
                Constraint = constraint,
            };
        }

        var wanted = SemanticVersion.Parse(addon.Version);

        if (installed.TryGetValue(addon.Guid, out var existing) && existing.IsAddon && existing.Version == wanted)
        {
            return Item(ImportAction.AlreadyInstalled, $"Version {addon.Version} is already installed.");
        }

        var parent = items.FirstOrDefault(item =>
            item.Kind == ImportItemKind.Mod && string.Equals(item.Id, addon.ParentGuid, StringComparison.OrdinalIgnoreCase)
        );

        var parentUsable =
            installed.ContainsKey(addon.ParentGuid)
            || parent is { Action: ImportAction.Install or ImportAction.Replace or ImportAction.AlreadyInstalled };

        if (!parentUsable)
        {
            return Item(ImportAction.Unavailable, $"Its mod '{addon.ParentGuid}' cannot be installed.");
        }

        if (addon.ForgeId is not { } forgeId)
        {
            return Item(ImportAction.Unavailable, "It did not come from the Forge, so it cannot be downloaded.");
        }

        IReadOnlyList<ForgeAddonVersion> versions;
        try
        {
            versions = await forge.GetAddonVersionsAsync(forgeId, cancellationToken);
        }
        catch (ForgeException exception)
        {
            return Item(ImportAction.Unavailable, $"The Forge could not be reached: {exception.Message}");
        }

        var match = PickAddonVersion(versions, addon.Version, wanted);
        if (match is null)
        {
            return Item(ImportAction.Unavailable, $"Version {addon.Version} is no longer on the Forge.");
        }

        if (!IsHttps(match.DownloadUrl))
        {
            return Item(ImportAction.Unavailable, "The Forge returned a download address that is not https; it was not trusted.");
        }

        var parentVersion = manifest
            .Mods.First(mod => string.Equals(mod.Guid, addon.ParentGuid, StringComparison.OrdinalIgnoreCase))
            .Version;
        if (!match.ModVersionConstraint.Satisfies(SemanticVersion.Parse(parentVersion)))
        {
            return Item(
                ImportAction.Unavailable,
                $"It supports mod versions '{match.ModVersionConstraint.Raw}', but the package's mod is {parentVersion}."
            );
        }

        return Item(ImportAction.Install, $"Download and install {addon.Version}.", match.DownloadUrl, match.ModVersionConstraint.Raw);
    }

    private ImportPlanItem PlanConfig(
        PackConfig config,
        byte[] content,
        Instance instance,
        Dictionary<string, InstalledMod> installed,
        List<ImportPlanItem> items
    )
    {
        ImportPlanItem Item(ImportAction action, string reason)
        {
            return new ImportPlanItem
            {
                Kind = ImportItemKind.Config,
                Id = config.Path,
                Name = config.Path,
                Action = action,
                Reason = reason,
                ParentGuid = config.ModGuid,
            };
        }

        installed.TryGetValue(config.ModGuid, out var owner);
        var ownerItem = items.FirstOrDefault(item =>
            item.Kind != ImportItemKind.Config && string.Equals(item.Id, config.ModGuid, StringComparison.OrdinalIgnoreCase)
        );

        if (owner is null)
        {
            return ownerItem is { Action: ImportAction.Install }
                ? Item(ImportAction.ApplyConfig, $"Written after '{config.ModGuid}' is installed.")
                : Item(ImportAction.Unavailable, $"'{config.ModGuid}' is not installed and cannot be.");
        }

        var row = owner.Files.FirstOrDefault(file => string.Equals(file.RelativePath, config.Path, StringComparison.Ordinal));
        if (row is null)
        {
            return Item(ImportAction.Unavailable, $"It is not a file of '{config.ModGuid}', so the package may not write it.");
        }

        var full = Path.Combine(instance.Path, config.Path);
        var current = File.Exists(full) ? FileHash.TryCompute(full) : null;
        var shared = Convert.ToHexStringLower(SHA256.HashData(content));

        if (string.Equals(current, shared, StringComparison.OrdinalIgnoreCase))
        {
            return Item(ImportAction.ConfigUpToDate, "It already has the shared content.");
        }

        var isEdited =
            current is not null
            && !string.Equals(current, row.OriginalSha256, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(current, row.Sha256, StringComparison.OrdinalIgnoreCase);

        return isEdited
            ? Item(ImportAction.KeepLocalConfig, "You edited this file; your version is kept unless you choose to overwrite it.")
            : Item(ImportAction.ApplyConfig, "Replace the file with the shared content.");
    }

    private async Task<ImportItemResult> ApplyModAsync(
        Instance instance,
        ImportPlanItem item,
        PackMod packMod,
        ImportOptions options,
        bool compareContent,
        IProgress<string>? progress,
        List<string> warnings,
        CancellationToken cancellationToken
    )
    {
        ImportItemResult Done(ImportOutcome outcome, string message, bool verified = false)
        {
            return new ImportItemResult
            {
                Kind = ImportItemKind.Mod,
                Id = item.Id,
                Outcome = outcome,
                Message = message,
                Verified = verified,
            };
        }

        switch (item.Action)
        {
            case ImportAction.AlreadyInstalled:
                return Done(ImportOutcome.Skipped, item.Reason);
            case ImportAction.Unavailable:
                return Done(ImportOutcome.Failed, item.Reason);
            case ImportAction.Replace when !options.ReplaceDifferentVersions:
                return Done(ImportOutcome.Skipped, $"{item.Reason} Replacing was not requested.");
        }

        progress?.Report($"Installing {item.Name} {item.Version}");

        try
        {
            var result = await installer.InstallAsync(
                new ModInstallRequest
                {
                    InstanceId = instance.Id,
                    DownloadUrl = new Uri(item.DownloadUrl!),
                    ForgeId = item.ForgeId,
                    ExpectedVersion = SemanticVersion.Parse(packMod.Version),
                    Pin = packMod.Pinned,
                },
                cancellationToken
            );

            if (!string.Equals(result.Guid, packMod.Guid, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"'{item.Name}' installed as '{result.Guid}', but the package calls it '{packMod.Guid}'.");
            }

            var outcome = item.Action == ImportAction.Replace ? ImportOutcome.Replaced : ImportOutcome.Installed;

            return await VerifyInstalledAsync(
                instance,
                result.Guid,
                packMod.ContentSha256,
                compareContent && !options.AcceptChangedContent,
                rollback: item.Action == ImportAction.Install,
                outcome,
                item,
                cancellationToken
            );
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            logger.LogWarning(exception, "Installing {Mod} from the package failed", item.Id);

            return Done(ImportOutcome.Failed, exception.Message);
        }
    }

    private async Task<ImportItemResult> ApplyAddonAsync(
        Instance instance,
        ImportPlanItem item,
        PackAddon packAddon,
        bool compareContent,
        IProgress<string>? progress,
        CancellationToken cancellationToken
    )
    {
        ImportItemResult Done(ImportOutcome outcome, string message, bool verified = false)
        {
            return new ImportItemResult
            {
                Kind = ImportItemKind.Addon,
                Id = item.Id,
                Outcome = outcome,
                Message = message,
                Verified = verified,
            };
        }

        if (item.Action == ImportAction.AlreadyInstalled)
        {
            return Done(ImportOutcome.Skipped, item.Reason);
        }

        if (item.Action != ImportAction.Install)
        {
            return Done(ImportOutcome.Failed, item.Reason);
        }

        progress?.Report($"Installing addon {item.Name} {item.Version}");

        try
        {
            var result = await addons.InstallAsync(
                new AddonInstallRequest
                {
                    InstanceId = instance.Id,
                    ParentModGuid = item.ParentGuid!,
                    ForgeId = item.ForgeId,
                    DownloadUrl = new Uri(item.DownloadUrl!),
                    Version = SemanticVersion.Parse(packAddon.Version),
                    ModVersionConstraint = item.Constraint is null ? null : VersionRange.Parse(item.Constraint),
                },
                cancellationToken
            );

            return await VerifyInstalledAsync(
                instance,
                result.Guid,
                packAddon.ContentSha256,
                compareContent,
                rollback: true,
                ImportOutcome.Installed,
                item,
                cancellationToken
            );
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            logger.LogWarning(exception, "Installing addon {Addon} from the package failed", item.Id);

            return Done(ImportOutcome.Failed, exception.Message);
        }
    }

    /// <summary>
    /// Compares what was installed with what the exporter had. A host that serves different content under the
    /// same version is exactly what this catches; a fresh install that fails the check is removed again.
    /// </summary>
    private async Task<ImportItemResult> VerifyInstalledAsync(
        Instance instance,
        string installedGuid,
        string expectedDigest,
        bool compare,
        bool rollback,
        ImportOutcome success,
        ImportPlanItem item,
        CancellationToken cancellationToken
    )
    {
        if (!compare || expectedDigest.Length == 0)
        {
            return new ImportItemResult
            {
                Kind = item.Kind,
                Id = item.Id,
                Outcome = success,
                Message =
                    expectedDigest.Length == 0
                        ? "Installed (the package carries no content digest to compare)."
                        : "Installed (content not compared).",
            };
        }

        var row = await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .InstalledMods.AsNoTracking()
                    .Include(mod => mod.Files)
                    .SingleOrDefaultAsync(mod => mod.InstanceId == instance.Id && mod.Guid == installedGuid, token),
            cancellationToken
        );

        if (row is not null && string.Equals(ContentDigest(row), expectedDigest, StringComparison.Ordinal))
        {
            return new ImportItemResult
            {
                Kind = item.Kind,
                Id = item.Id,
                Outcome = success,
                Message = "Installed; the content matches what the package was made from.",
                Verified = true,
            };
        }

        const string Mismatch = "The downloaded content is not what the package was made from (the host may have changed the file).";

        if (rollback && row is not null)
        {
            try
            {
                await installer.UninstallAsync(instance.Id, installedGuid, force: true, cancellationToken);

                return Failure(item, $"{Mismatch} It was removed again; use the option to accept changed content if you trust it.");
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                logger.LogWarning(exception, "Could not roll back {Mod}", installedGuid);
            }
        }

        return Failure(item, $"{Mismatch} It was left installed.");
    }

    private async Task<ImportItemResult> ApplyConfigAsync(
        Instance instance,
        ImportPlanItem item,
        PackConfig config,
        byte[] content,
        ImportOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken
    )
    {
        ImportItemResult Done(ImportOutcome outcome, string message, bool verified = false)
        {
            return new ImportItemResult
            {
                Kind = ImportItemKind.Config,
                Id = config.Path,
                Outcome = outcome,
                Message = message,
                Verified = verified,
            };
        }

        if (!options.ApplyConfigs)
        {
            return Done(ImportOutcome.Skipped, "Config files were not requested.");
        }

        switch (item.Action)
        {
            case ImportAction.ConfigUpToDate:
                return Done(ImportOutcome.Skipped, item.Reason);
            case ImportAction.Unavailable:
                return Done(ImportOutcome.Failed, item.Reason);
            case ImportAction.KeepLocalConfig when !options.OverwriteEditedConfigs:
                return Done(ImportOutcome.Skipped, item.Reason);
        }

        try
        {
            var row = await unitOfWork.ExecuteAsync(
                async (context, token) =>
                    await context
                        .InstalledMods.AsNoTracking()
                        .Include(mod => mod.Files)
                        .SingleOrDefaultAsync(mod => mod.InstanceId == instance.Id && mod.Guid == config.ModGuid, token),
                cancellationToken
            );

            // Ownership is checked against what is installed *now*: the package cannot write a file no mod owns.
            var known = row?.Files.FirstOrDefault(file => string.Equals(file.RelativePath, config.Path, StringComparison.Ordinal));
            if (row is null || known is null)
            {
                return Done(ImportOutcome.Failed, $"'{config.Path}' is not a config file of '{config.ModGuid}'; it was not written.");
            }

            var target = PackPathGuard.Resolve(instance.Path, config.Path);
            progress?.Report($"Writing {config.Path}");

            await writer.WriteAllBytesAsync(target, content, cancellationToken);

            var written = FileHash.TryCompute(target);
            if (!string.Equals(written, config.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Done(ImportOutcome.Failed, $"'{config.Path}' does not match the shared hash after writing.");
            }

            await unitOfWork.ExecuteAsync(
                async (context, token) =>
                {
                    var file = await context.InstalledFiles.SingleAsync(entity => entity.Id == known.Id, token);
                    file.Sha256 = config.Sha256;
                    file.Length = content.Length;
                },
                cancellationToken
            );

            return Done(ImportOutcome.ConfigApplied, "Written and verified.", verified: true);
        }
        catch (PackException exception)
        {
            return Done(ImportOutcome.Failed, exception.Message);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            logger.LogWarning(exception, "Writing {Config} failed", config.Path);

            return Done(ImportOutcome.Failed, exception.Message);
        }
    }

    private static ImportItemResult Failure(ImportPlanItem item, string message)
    {
        return new ImportItemResult
        {
            Kind = item.Kind,
            Id = item.Id,
            Outcome = ImportOutcome.Failed,
            Message = message,
        };
    }

    private static bool IsRecoverable(Exception exception)
    {
        return exception
            is InstanceValidationException
                or ForgeException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or HttpRequestException;
    }

    /// <summary>
    /// The version the package named: the exact published string when there is one (two releases can differ
    /// only in build metadata, which semver ignores), otherwise the first with the same precedence.
    /// </summary>
    private static ForgeModVersion? PickVersion(IReadOnlyList<ForgeModVersion> versions, string text, SemanticVersion wanted)
    {
        return versions.FirstOrDefault(version => version.Version.ToString() == text)
            ?? versions.FirstOrDefault(version => version.Version == wanted);
    }

    private static ForgeAddonVersion? PickAddonVersion(IReadOnlyList<ForgeAddonVersion> versions, string text, SemanticVersion wanted)
    {
        return versions.FirstOrDefault(version => version.Version.ToString() == text)
            ?? versions.FirstOrDefault(version => version.Version == wanted);
    }

    private static bool IsHttps(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// Digest of every file a mod shipped: sorted <c>path:sha256</c> lines using the hash <em>as shipped</em>
    /// (<see cref="InstalledFile.OriginalSha256" />). Edits and imported configs never change it, so a
    /// re-export of an imported instance reproduces the original digest and share code.
    /// </summary>
    internal static string ContentDigest(InstalledMod mod)
    {
        var lines = mod
            .Files.Select(file => $"{file.RelativePath.Replace('\\', '/')}:{(file.OriginalSha256 ?? file.Sha256).ToLowerInvariant()}")
            .Order(StringComparer.Ordinal);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static string AppVersion()
    {
        var version = typeof(PackService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }

    private async Task<Instance> LoadInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .Instances.AsNoTracking()
                    .Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Files)
                    .Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Mod)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token),
            cancellationToken
        );

        return instance ?? throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
    }

    private sealed record ConfigCandidate(PackConfig Config, byte[] Content);
}
