using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Patching;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Adopts an install that is already on the machine, in place. The instance is the folder the user pointed at (nothing
/// in it is moved, rewritten or deleted); the files a pristine install proves are game or SPT files are hard-linked
/// into a base install that other instances can share, and everything else is reported as a modification.
/// </summary>
/// <remarks>
/// Hard links are what makes this free: the adopted instance and the new base install are two names for the same
/// inodes, so a 60 GB install costs no extra disk. A file only becomes a link once its SHA-256 equals the reference's,
/// which is the point of hashing at all.
/// </remarks>
public sealed class AdoptService(
    IInstallDetector detector,
    IFileLinkService links,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<AdoptService> logger
) : IAdoptService
{
    /// <summary>Files whose hash is compared at once. SHA-256 is CPU-bound, so a few threads keep a fast disk busy.</summary>
    private const int HashParallelism = 4;

    /// <summary>How many modifications the plan lists by name before it stops collecting them.</summary>
    private const int ModificationSample = 200;

    /// <summary>Instance state that is never shared, never counted as a modification, and simply stays where it is.</summary>
    private static readonly string[] StateRoots = ["BepInEx/config/", "BepInEx/cache/", "Logs/", "SPT_Runtime/user/", "SPT/user/", "user/"];

    /// <inheritdoc />
    public async Task<AdoptPlan> PlanAsync(AdoptRequest request, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var warnings = new List<string>();
        var issues = new List<string>();

        var install = detector.Detect(request.InstallPath);
        if (!install.IsInstall)
        {
            issues.AddRange(install.Warnings.Count > 0 ? install.Warnings : [$"'{request.InstallPath}' is not an SPT install."]);
            return new AdoptPlan
            {
                Install = install,
                Issues = issues,
                Warnings = warnings,
            };
        }

        if (!install.IsRunnable)
        {
            issues.Add(
                $"'{install.RootPath}' is a {install.Shape} install, which SPT cannot run. Point at the folder that holds 'EscapeFromTarkov.exe' at its root."
            );
        }

        var reference = ResolveReference(request.ReferenceBasePath, install, warnings, issues);
        var files = EnumerateFiles(install.RootPath, cancellationToken);
        logger.LogDebug(
            "Adopt plan: {Files} files in {Root}; reference {Reference}",
            files.Count,
            install.RootPath,
            reference?.RootPath ?? "none"
        );

        var referenceIndex = reference is null ? null : BuildIndex(reference);
        var classification = await ClassifyAsync(files, referenceIndex, cancellationToken);
        var basePath = reference is not null ? reference.RootPath : BasePathFor(request.BaseRoot, install);

        if (reference is null && Directory.Exists(basePath))
        {
            warnings.Add($"'{basePath}' already exists and is used as the base install without being rewritten.");
        }

        if (reference is not null && PathsAreInside(install.RootPath, request.BaseRoot))
        {
            warnings.Add("The base root is inside the adopted install; the base install will be left where it is.");
        }

        var plan = new AdoptPlan
        {
            Install = install,
            ReferencePath = reference?.RootPath,
            SharedFiles = classification.Shared.Count,
            PatchedFiles = classification.Patched.Count,
            ModifiedFiles = classification.Modified.Count,
            ExtraFiles = classification.Extra.Count,
            StateFiles = classification.State.Count,
            SharedBytes = classification.Shared.Sum(entry => entry.Size),
            ModifiedBytes = classification.Modified.Sum(entry => entry.Size) + classification.Extra.Sum(entry => entry.Size),
            Modifications = classification
                .Modified.Concat(classification.Extra)
                .Take(ModificationSample)
                .Select(entry => entry.Relative)
                .ToList(),
            CreatesBase = reference is null,
            BasePath = basePath,
            Issues = issues,
            Warnings = reference is null ? [.. warnings, NoReferenceWarning(install)] : warnings,
        };

        logger.LogDebug(
            "Adopt plan done in {Elapsed} ms: {Shared} shared, {Patched} patched, {Modified} modified, {Extra} extra, {State} state files; base {Base}",
            started.ElapsedMilliseconds,
            plan.SharedFiles,
            plan.PatchedFiles,
            plan.ModifiedFiles,
            plan.ExtraFiles,
            plan.StateFiles,
            plan.BasePath ?? "none"
        );

        return plan;
    }

    /// <inheritdoc />
    public async Task<Instance> AdoptAsync(
        AdoptRequest request,
        IProgress<InstanceCreationProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var plan = await PlanAsync(request, cancellationToken);
        if (plan.Issues.Count > 0)
        {
            throw new InstanceValidationException(plan.Issues);
        }

        var install = plan.Install;
        var existing = await unitOfWork.ExecuteAsync(
            async (context, token) => await context.Instances.FirstOrDefaultAsync(row => row.Path == install.RootPath, token),
            cancellationToken
        );

        if (existing is not null)
        {
            throw new InstanceValidationException([$"'{install.RootPath}' is already the instance '{existing.Name}'."]);
        }

        progress?.Report(new InstanceCreationProgress("Preparing the base install", 0, 1));
        var files = EnumerateFiles(install.RootPath, cancellationToken);
        var reference = plan.ReferencePath is null ? null : detector.Detect(plan.ReferencePath);
        var index = reference is null ? null : BuildIndex(reference);
        var classification = await ClassifyAsync(files, index, cancellationToken);
        var basePath = plan.BasePath ?? BasePathFor(request.BaseRoot, install);

        if (plan.CreatesBase)
        {
            PopulateBase(basePath, install.RootPath, classification, progress, cancellationToken);
        }

        progress?.Report(new InstanceCreationProgress("Recording the instance", 1, 1));
        var instance = await RecordAsync(request, install, basePath, cancellationToken);

        logger.LogInformation(
            "Adopted {Path} as '{Name}' in {Elapsed} ms: base {Base}, {Shared} shared, {Modified} modified",
            install.RootPath,
            instance.Name,
            (DateTimeOffset.UtcNow - instance.CreatedAt).TotalMilliseconds,
            basePath,
            classification.Shared.Count,
            classification.Modified.Count + classification.Extra.Count
        );

        return instance;
    }

    /// <summary>Writes the base install: one hard link per pristine file, a copy when the volumes differ.</summary>
    private void PopulateBase(
        string basePath,
        string installRoot,
        Classification classification,
        IProgress<InstanceCreationProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var sameVolume = links.IsSameVolume(basePath, installRoot);
        var entries = classification.Shared.Concat(classification.Patched).ToList();
        Directory.CreateDirectory(basePath);
        logger.LogDebug("Populating base {Base} with {Entries} files ({Volume})", basePath, entries.Count, sameVolume ? "links" : "copies");

        var completed = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A patched file is stored in its pristine form: the instance keeps the patched copy and the backup.
            var source = entry.Kind == AdoptFileKind.Patched ? entry.Source + ".spt-bak" : entry.Source;
            var destination = Path.Combine(basePath, entry.Relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (File.Exists(destination))
            {
                completed++;
                continue;
            }

            if (sameVolume)
            {
                links.CreateHardLink(destination, source);
            }
            else
            {
                links.CopyFile(source, destination, overwrite: false);
            }

            completed++;
            if (completed % 250 == 0)
            {
                progress?.Report(new InstanceCreationProgress("Building the base install", completed, entries.Count));
            }
        }

        progress?.Report(new InstanceCreationProgress("Building the base install", entries.Count, entries.Count));
    }

    /// <summary>Stores the instance row; the folder stays exactly where it is.</summary>
    private async Task<Instance> RecordAsync(
        AdoptRequest request,
        InstallFingerprint install,
        string basePath,
        CancellationToken cancellationToken
    )
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? NameFrom(install.RootPath) : request.Name.Trim();
        var patch = install.Patch;

        var instance = new Instance
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = install.RootPath,
            BaseGamePath = basePath,
            SptVersion = install.SptVersion ?? new SemanticVersion(0, 0, 0),
            TarkovVersion = install.ActualTarkovVersion ?? "unknown",
            Layout = install.Layout ?? InstanceLayout.Spt41Plus,
            ServerPort = 6969,
            CreatedAt = time.GetUtcNow(),
            Patch = new PatchInfo
            {
                IsPatched = patch?.State == PatchState.Patched,
                OriginalSha256 = patch?.BackupSha256,
                PatchedSha256 = patch?.TargetSha256,
                BackupPath = patch?.BackupPath,
                AppliedAt = patch?.State == PatchState.Patched ? time.GetUtcNow() : null,
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
                        UpdatedAt = time.GetUtcNow(),
                    }
                );
                await Task.CompletedTask;
            },
            cancellationToken
        );

        return instance;
    }

    /// <summary>
    /// Compares every file with the reference. A hash is only computed when it can change the answer: the size decides
    /// first, and a file whose instance copy has a <c>.spt-bak</c> next to it is compared through that backup, because
    /// the patched form of a shared file always differs from the pristine one.
    /// </summary>
    private static async Task<Classification> ClassifyAsync(
        IReadOnlyList<SourceFile> files,
        IReadOnlyDictionary<string, ReferenceFile>? reference,
        CancellationToken cancellationToken
    )
    {
        var result = new Classification();
        if (reference is null)
        {
            foreach (var file in files)
            {
                if (IsStatePath(file.Relative))
                {
                    result.State.Add(new AdoptEntry(file.Relative, file.FullPath, file.Size, AdoptFileKind.State));
                }
                else
                {
                    // Nothing to prove otherwise: the install becomes its own base, linkable files stay linkable.
                    result.Shared.Add(new AdoptEntry(file.Relative, file.FullPath, file.Size, AdoptFileKind.Shared));
                }
            }

            return result;
        }

        var candidates = new List<(SourceFile File, ReferenceFile Match)>();
        foreach (var file in files)
        {
            if (IsStatePath(file.Relative))
            {
                result.State.Add(new AdoptEntry(file.Relative, file.FullPath, file.Size, AdoptFileKind.State));
            }
            else if (reference.TryGetValue(file.Relative, out var match))
            {
                candidates.Add((file, match));
            }
            else
            {
                result.Extra.Add(new AdoptEntry(file.Relative, file.FullPath, file.Size, AdoptFileKind.Extra));
            }
        }

        await Parallel
            .ForEachAsync(
                candidates,
                new ParallelOptions { MaxDegreeOfParallelism = HashParallelism, CancellationToken = cancellationToken },
                async (candidate, token) =>
                {
                    var entry = new AdoptEntry(
                        candidate.File.Relative,
                        candidate.File.FullPath,
                        candidate.File.Size,
                        AdoptFileKind.Modified
                    );
                    result.Add(entry with { Kind = await KindAsync(candidate.File, candidate.Match, token) });
                }
            )
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// What one file is, given the reference's file at the same path: shared, the patched form of a shared file, or a
    /// modification.
    /// </summary>
    private static async Task<AdoptFileKind> KindAsync(SourceFile file, ReferenceFile match, CancellationToken cancellationToken)
    {
        var backup = file.FullPath + ".spt-bak";
        var backupExists = File.Exists(backup);

        if (match.Size == file.Size)
        {
            var instanceHash = await FileHash.ComputeAsync(file.FullPath, cancellationToken);
            if (string.Equals(instanceHash, await FileHash.ComputeAsync(match.FullPath, cancellationToken), StringComparison.Ordinal))
            {
                return AdoptFileKind.Shared;
            }

            if (backupExists && new FileInfo(backup).Length == file.Size)
            {
                return string.Equals(await FileHash.ComputeAsync(backup, cancellationToken), instanceHash, StringComparison.Ordinal)
                    ? AdoptFileKind.Patched
                    : AdoptFileKind.Modified;
            }

            return AdoptFileKind.Modified;
        }

        // Different sizes: the backup, when it exists, is the pristine form this instance was patched from.
        if (backupExists && new FileInfo(backup).Length == match.Size)
        {
            return string.Equals(
                await FileHash.ComputeAsync(backup, cancellationToken),
                await FileHash.ComputeAsync(match.FullPath, cancellationToken),
                StringComparison.Ordinal
            )
                ? AdoptFileKind.Patched
                : AdoptFileKind.Modified;
        }

        return AdoptFileKind.Modified;
    }

    /// <summary>Every file of the install, relative to its root, sorted so the plan is stable.</summary>
    private static List<SourceFile> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var files = new List<SourceFile>();

        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, path);

            // The launcher's own scratch area is not part of an install.
            if (relative.StartsWith(".ytt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            files.Add(new SourceFile(relative.Replace(Path.DirectorySeparatorChar, '/'), path, size));
        }

        files.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));
        return files;
    }

    /// <summary>Indexes a reference by merged-relative path, unwrapping a split install.</summary>
    private static Dictionary<string, ReferenceFile> BuildIndex(InstallFingerprint reference)
    {
        var index = new Dictionary<string, ReferenceFile>(StringComparer.OrdinalIgnoreCase);
        var shape = reference.Shape ?? InstallShape.Merged;

        foreach (var file in EnumerateFiles(reference.RootPath, CancellationToken.None))
        {
            var relative = Merge(shape, file.Relative);
            if (relative is not null)
            {
                index[relative] = new ReferenceFile(file.FullPath, file.Size);
            }
        }

        return index;
    }

    /// <summary>Turns a reference path into the path the same file has in a merged install.</summary>
    private static string? Merge(InstallShape shape, string relative)
    {
        if (shape == InstallShape.Merged)
        {
            return relative;
        }

        if (relative.StartsWith("gamefiles/", StringComparison.OrdinalIgnoreCase))
        {
            return relative["gamefiles/".Length..];
        }

        return relative.StartsWith("spt/", StringComparison.OrdinalIgnoreCase) ? relative["spt/".Length..] : null;
    }

    /// <summary>The reference to compare against: the given one, or a base of the same game build in the base root.</summary>
    private InstallFingerprint? ResolveReference(
        string? explicitPath,
        InstallFingerprint install,
        List<string> warnings,
        List<string> issues
    )
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var reference = detector.Detect(explicitPath);
            if (!reference.IsInstall)
            {
                issues.Add($"The reference install '{explicitPath}' is not an SPT install.");
                return null;
            }

            if (reference.Shape != InstallShape.Split && !reference.RootPath.Equals(install.RootPath, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"The reference '{reference.RootPath}' is a merged install, so its mods cannot be told apart from the base files."
                );
            }

            return reference;
        }

        return null;
    }

    /// <summary>Where a base install for this install would be created, named after its versions.</summary>
    private static string BasePathFor(string baseRoot, InstallFingerprint install)
    {
        var name = install.SptVersion?.ToString() is { Length: > 0 } version ? $"base-{version}" : "base-unknown";
        if (install.ActualTarkovVersion is { Length: > 0 } tarkov)
        {
            name += $"-eft-{tarkov}";
        }

        return Path.Combine(Path.GetFullPath(baseRoot), name);
    }

    private static bool PathsAreInside(string path, string candidate)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return (Path.GetFullPath(candidate) + Path.DirectorySeparatorChar).StartsWith(full, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The folder name with anything that cannot name an instance replaced by a dash.</summary>
    private static string NameFrom(string root)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        var cleaned = new string([
            .. name.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '-'),
        ]).Trim('-');
        return string.IsNullOrEmpty(cleaned) ? "instance" : cleaned;
    }

    /// <summary>True for per-instance state: profiles, logs, configuration and caches are never shared.</summary>
    private static bool IsStatePath(string relative)
    {
        return relative.EndsWith(".spt-bak", StringComparison.OrdinalIgnoreCase)
            || StateRoots.Any(root => relative.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static string NoReferenceWarning(InstallFingerprint install)
    {
        return $"No pristine install was given, so every file of '{install.RootPath}' is treated as a base file and hard-linked as it is: mods, profiles and configuration included.";
    }

    /// <summary>One file of the install, relative to its root.</summary>
    private sealed record SourceFile(string Relative, string FullPath, long Size);

    private sealed record ReferenceFile(string FullPath, long Size);

    private sealed record AdoptEntry(string Relative, string Source, long Size, AdoptFileKind Kind);

    private sealed class Classification
    {
        public List<AdoptEntry> Shared { get; } = [];

        public List<AdoptEntry> Patched { get; } = [];

        public List<AdoptEntry> Modified { get; } = [];

        public List<AdoptEntry> Extra { get; } = [];

        public List<AdoptEntry> State { get; } = [];

        /// <summary>Routes an entry to its list; called from the parallel hash pass, so it locks.</summary>
        public void Add(AdoptEntry entry)
        {
            lock (this)
            {
                switch (entry.Kind)
                {
                    case AdoptFileKind.Shared:
                        Shared.Add(entry);
                        break;
                    case AdoptFileKind.Patched:
                        Patched.Add(entry);
                        break;
                    default:
                        Modified.Add(entry);
                        break;
                }
            }
        }
    }
}

/// <summary>How one file of an adopted install relates to the pristine reference.</summary>
public enum AdoptFileKind
{
    /// <summary>Hash equals the reference's: a game or SPT file, shared by hard link.</summary>
    Shared,

    /// <summary>The instance holds the patched form; the reference's file is the pristine one.</summary>
    Patched,

    /// <summary>Differs from the reference: a mod, an edit, a configuration file.</summary>
    Modified,

    /// <summary>The reference does not have it.</summary>
    Extra,

    /// <summary>Per-instance state: never shared, never counted as a change.</summary>
    State,
}
