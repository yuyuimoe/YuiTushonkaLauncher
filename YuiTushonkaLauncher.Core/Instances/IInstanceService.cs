using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Instances;

/// <summary>Everything needed to create an instance.</summary>
public sealed record InstanceCreationRequest
{
    /// <summary>Display name and folder name, restricted to <c>[A-Za-z0-9_-]+</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Parent directory the instance folder is created in.</summary>
    public required string Location { get; init; }

    /// <summary>Absolute path of the user-supplied install produced by the SPT installer.</summary>
    public required string BaseInstallPath { get; init; }
}

/// <summary>One entry of the plan: what happens to a path, where it lands, and how big it is.</summary>
/// <param name="RelativePath">Path relative to the base install.</param>
/// <param name="Action">What the builder does with it.</param>
/// <param name="SizeBytes">Size, counted only for copies.</param>
/// <param name="DestinationPath">
/// Path relative to the instance, when it differs from <paramref name="RelativePath" />: a split base is
/// unwrapped (<c>gamefiles/x</c> and <c>spt/x</c> both land at <c>x</c>).
/// </param>
public sealed record InstancePlanEntry(string RelativePath, LinkAction Action, long SizeBytes, string? DestinationPath = null)
{
    /// <summary>Where the entry lands in the instance; the base path itself when the plan did not say.</summary>
    public string Destination
    {
        get { return DestinationPath ?? RelativePath; }
    }
}

/// <summary>The pre-flight result: what would be linked, copied or skipped, and what would fail.</summary>
public sealed record InstancePlan
{
    public required string InstancePath { get; init; }

    public required InstanceLayout Layout { get; init; }

    public IReadOnlyList<InstancePlanEntry> Entries { get; init; } = [];

    /// <summary>Directories created empty (per-instance state), relative to the instance.</summary>
    public IReadOnlyList<string> Directories { get; init; } = [];

    /// <summary>Shape of the base install the plan was made from.</summary>
    public InstallShape BaseShape { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Bytes that will be physically copied (the rest is shared).</summary>
    public long CopyBytes
    {
        get { return Entries.Where(entry => entry.Action == LinkAction.Copy).Sum(entry => entry.SizeBytes); }
    }

    /// <summary>Bytes that will be shared through hard links.</summary>
    public long LinkBytes
    {
        get { return Entries.Where(entry => entry.Action == LinkAction.Link).Sum(entry => entry.SizeBytes); }
    }

    public int LinkCount
    {
        get { return Entries.Count(entry => entry.Action == LinkAction.Link); }
    }

    public int CopyCount
    {
        get { return Entries.Count(entry => entry.Action == LinkAction.Copy); }
    }

    public int SkipCount
    {
        get { return Entries.Count(entry => entry.Action == LinkAction.Skip); }
    }
}

/// <summary>Progress of a long-running instance operation.</summary>
public readonly record struct InstanceCreationProgress(string Step, int Completed, int Total)
{
    /// <summary>Completion between 0 and 1.</summary>
    public double Fraction
    {
        get { return Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1); }
    }
}

/// <summary>Raised when an instance operation cannot proceed; carries every reason found.</summary>
public sealed class InstanceValidationException : Exception
{
    public InstanceValidationException(IReadOnlyList<string> issues)
        : base(string.Join(" ", issues))
    {
        Issues = issues;
    }

    /// <summary>All problems found, each already phrased for the user.</summary>
    public IReadOnlyList<string> Issues { get; }
}

/// <summary>Instance lifecycle: create from a base install, list, rename, delete.</summary>
public interface IInstanceService
{
    /// <summary>Instances known to the database, newest first.</summary>
    Task<IReadOnlyList<Instance>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates a creation request and describes what would happen.</summary>
    /// <exception cref="InstanceValidationException">The request cannot be fulfilled.</exception>
    Task<InstancePlan> PlanAsync(InstanceCreationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates the instance, its files and its database row.</summary>
    Task<Instance> CreateAsync(
        InstanceCreationRequest request,
        IProgress<InstanceCreationProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes an instance, optionally removing its files.</summary>
    Task DeleteAsync(Guid instanceId, bool deleteFiles, CancellationToken cancellationToken = default);

    /// <summary>Renames an instance and its folder.</summary>
    Task RenameAsync(Guid instanceId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Re-reads the patch state of an instance from disk and stores it.</summary>
    Task<PatchStateInfo> DetectPatchStateAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
