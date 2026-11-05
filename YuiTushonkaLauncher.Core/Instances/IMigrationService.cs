using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Instances;

/// <summary>How a scanned mod was matched against the Forge catalogue.</summary>
public enum MigrationMatch
{
    /// <summary>GUID found in the catalogue.</summary>
    Recognized,

    /// <summary>GUID read from the files but unknown to Forge.</summary>
    UnknownToForge,

    /// <summary>No GUID could be read from the files.</summary>
    GuidMissing,
}

/// <summary>One mod found by the migration scan.</summary>
public sealed record MigrationEntry
{
    /// <summary>GUID read from the installed files, when there is one.</summary>
    public string? Guid { get; init; }

    /// <summary>Name reported by the DLL, the folder or Forge.</summary>
    public required string Name { get; init; }

    public SemanticVersion? Version { get; init; }

    public ModKind Kind { get; init; }

    public MigrationMatch Match { get; init; }

    /// <summary>Forge id, once matched.</summary>
    public int? ForgeId { get; init; }

    public string? Author { get; init; }

    /// <summary>Files the mod owns, relative to the instance root.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>True when the same GUID was found twice (two folders, same mod).</summary>
    public bool IsDuplicate { get; init; }
}

/// <summary>Result of scanning an installation before adopting it.</summary>
public sealed record MigrationPlan
{
    public required Guid InstanceId { get; init; }

    public required string InstancePath { get; init; }

    public IReadOnlyList<MigrationEntry> Recognized { get; init; } = [];

    public IReadOnlyList<MigrationEntry> Unrecognized { get; init; } = [];

    public IReadOnlyList<MigrationEntry> WithoutGuid { get; init; } = [];

    /// <summary>Files under the mod roots that belong to no identified mod.</summary>
    public IReadOnlyList<string> OrphanFiles { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public int TotalMods
    {
        get { return Recognized.Count + Unrecognized.Count + WithoutGuid.Count; }
    }
}

/// <summary>What applying a plan did.</summary>
public sealed record MigrationResult
{
    public required int Registered { get; init; }

    public required int Skipped { get; init; }

    public required int FilesHashed { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Adopts a hand-made SPT installation into a managed instance without moving any file.</summary>
public interface IMigrationService
{
    /// <summary>
    /// Registers an existing SPT folder as an instance (no linking, no copying) and scans it.
    /// </summary>
    /// <exception cref="InstanceValidationException">The folder is not a usable SPT install.</exception>
    Task<(Instance Instance, MigrationPlan Plan)> AdoptAsync(string path, string name, CancellationToken cancellationToken = default);

    /// <summary>Scans an already-managed instance and reports what it found.</summary>
    Task<MigrationPlan> ScanAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Registers the mods of a scanned plan, recording their files and hashes.</summary>
    Task<MigrationResult> ApplyAsync(MigrationPlan plan, CancellationToken cancellationToken = default);
}
