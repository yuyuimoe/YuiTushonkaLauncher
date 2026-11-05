using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Instances;

/// <summary>What to adopt: an install that is already on the machine, and where a base install may be created.</summary>
public sealed record AdoptRequest
{
    /// <summary>Absolute path of a playable (merged) SPT install.</summary>
    public required string InstallPath { get; init; }

    /// <summary>Directory that may receive a new base install when there is no reference to classify against.</summary>
    public required string BaseRoot { get; init; }

    /// <summary>
    /// Pristine install of the same game build to classify files against; <c>null</c> means "no reference", which
    /// makes the install its own base and records a warning.
    /// </summary>
    public string? ReferenceBasePath { get; init; }

    /// <summary>Display name; the folder name of an adopted instance is the one it already has.</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>What adopting would do: how the files were classified and where the base install would go.</summary>
public sealed record AdoptPlan
{
    /// <summary>What the folder turned out to be.</summary>
    public required InstallFingerprint Install { get; init; }

    /// <summary>Pristine install the files were compared against, when one was used.</summary>
    public string? ReferencePath { get; init; }

    /// <summary>Files the reference proves are game or SPT files, shared by hard link.</summary>
    public int SharedFiles { get; init; }

    /// <summary>Files the instance holds in their patched form (the reference's backup matches).</summary>
    public int PatchedFiles { get; init; }

    /// <summary>Files that differ from the reference in any other way: mods, edits, configuration.</summary>
    public int ModifiedFiles { get; init; }

    /// <summary>Files the reference does not have at all: added by a mod or by the user.</summary>
    public int ExtraFiles { get; init; }

    /// <summary>Per-instance state (profiles, logs, configs, caches) that is never shared.</summary>
    public int StateFiles { get; init; }

    public long SharedBytes { get; init; }

    public long ModifiedBytes { get; init; }

    /// <summary>A capped sample of what was kept as a modification, relative to the instance root.</summary>
    public IReadOnlyList<string> Modifications { get; init; } = [];

    /// <summary>True when a base install would be created.</summary>
    public bool CreatesBase { get; init; }

    /// <summary>Where that base install would go, when one is created.</summary>
    public string? BasePath { get; init; }

    /// <summary>Problems found: they stop the adopt, with every reason listed.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>Non-fatal findings worth showing.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool CanAdopt
    {
        get { return Issues.Count == 0; }
    }
}

/// <summary>
/// Brings an install that already exists on this machine into the launcher, in place: the instance is the folder the
/// user pointed at, and the pristine files are lifted into a base install that other instances can share.
/// </summary>
public interface IAdoptService
{
    /// <summary>Classifies the install without writing anything.</summary>
    Task<AdoptPlan> PlanAsync(AdoptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Adopts the install and stores the instance row.</summary>
    /// <exception cref="InstanceValidationException">The plan has issues.</exception>
    Task<Instance> AdoptAsync(
        AdoptRequest request,
        IProgress<InstanceCreationProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
