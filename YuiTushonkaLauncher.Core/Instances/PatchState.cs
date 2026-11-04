namespace YuiTushonkaLauncher.Core.Instances;

/// <summary>Patch state of an instance's game binaries.</summary>
public enum PatchState
{
    /// <summary>No backup and no known-vanilla hash: the launcher cannot tell.</summary>
    Unknown,

    /// <summary>Vanilla binary, or a backup that currently matches the target (restored).</summary>
    NotPatched,

    /// <summary>The target differs from its backup: the patch is applied.</summary>
    Patched,
}

/// <summary>What the detector found on disk.</summary>
public sealed record PatchStateInfo
{
    public required PatchState State { get; init; }

    /// <summary>Absolute path of the patched binary, when present.</summary>
    public string? TargetPath { get; init; }

    /// <summary>Absolute path of the <c>.spt-bak</c> file, when present.</summary>
    public string? BackupPath { get; init; }

    public string? TargetSha256 { get; init; }

    public string? BackupSha256 { get; init; }

    /// <summary>Human-readable reason, shown next to the state in the UI.</summary>
    public required string Reason { get; init; }
}
