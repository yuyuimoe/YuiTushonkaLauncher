using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>One file whose content no longer matches what was installed.</summary>
public sealed record FileDrift(string RelativePath, FileDriftKind Kind, long InstalledLength, long? CurrentLength)
{
    /// <summary>Path relative to the instance root.</summary>
    public string RelativePath { get; } = RelativePath;
}

/// <summary>How a file drifted from its installed state.</summary>
public enum FileDriftKind
{
    /// <summary>The file was edited (config edits are expected and exportable).</summary>
    Modified,

    /// <summary>The file is gone.</summary>
    Missing,
}

/// <summary>Integrity of one installed mod.</summary>
public sealed record ModIntegrity
{
    public required string ModGuid { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<FileDrift> Drifted { get; init; } = [];

    /// <summary>Configuration files the user edited, which an export would include.</summary>
    public IReadOnlyList<string> ModifiedConfigs { get; init; } = [];

    public bool IsClean
    {
        get { return Drifted.Count == 0; }
    }
}

/// <summary>Integrity report for a whole instance.</summary>
public sealed record IntegrityReport
{
    public required Guid InstanceId { get; init; }

    public IReadOnlyList<ModIntegrity> Mods { get; init; } = [];

    public bool IsClean
    {
        get { return Mods.All(mod => mod.IsClean); }
    }

    /// <summary>Every path that drifted, across all mods.</summary>
    public IReadOnlyList<string> DriftedPaths
    {
        get { return [.. Mods.SelectMany(mod => mod.Drifted).Select(drift => drift.RelativePath)]; }
    }
}

/// <summary>Compares what is on disk with the hashes recorded at install time.</summary>
public interface IIntegrityService
{
    /// <summary>Verifies every installed mod of an instance.</summary>
    Task<IntegrityReport> VerifyAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Verifies one installed mod.</summary>
    Task<ModIntegrity?> VerifyModAsync(Guid instanceId, string modGuid, CancellationToken cancellationToken = default);
}
