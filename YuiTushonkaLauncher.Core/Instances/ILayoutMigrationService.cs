namespace YuiTushonkaLauncher.Core.Instances;

/// <summary>One file that moves during a layout migration.</summary>
public sealed record LayoutMigrationEntry(string SourceRelativePath, string DestinationRelativePath, long SizeBytes);

/// <summary>The instance as it is, and what a migration would do.</summary>
public sealed record LayoutMigrationPlan
{
    public required Guid InstanceId { get; init; }

    public required string InstanceName { get; init; }

    public required string InstancePath { get; init; }

    public required InstallShape Shape { get; init; }

    /// <summary>Nothing to do: the instance already has the merged shape.</summary>
    public bool IsMigrated
    {
        get { return Shape == InstallShape.Merged; }
    }

    public IReadOnlyList<LayoutMigrationEntry> Entries { get; init; } = [];

    /// <summary>Directories the merged layout needs that the split one did not have at the root.</summary>
    public IReadOnlyList<string> Directories { get; init; } = [];

    /// <summary>Stored paths that will be rewritten (installed files, patch backup).</summary>
    public int StoredPathsToRewrite { get; init; }

    public IReadOnlyList<string> Issues { get; init; } = [];

    public long BytesMoved
    {
        get { return Entries.Sum(entry => entry.SizeBytes); }
    }

    public bool CanMigrate
    {
        get { return !IsMigrated && Issues.Count == 0; }
    }
}

public sealed record LayoutMigrationResult
{
    public required LayoutMigrationPlan Plan { get; init; }

    public int FilesMoved { get; init; }

    public int DirectoriesRemoved { get; init; }

    public int StoredPathsRewritten { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Migrates an instance created before the layout change: the split <c>gamefiles/</c> + <c>spt/</c> shape becomes
/// SPT's merged root, and the paths stored in the database follow.
/// </summary>
public interface ILayoutMigrationService
{
    /// <summary>The instances that still have the split shape.</summary>
    Task<IReadOnlyList<LayoutMigrationPlan>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Plans a migration. Reads only; refuses an instance whose server is running.</summary>
    Task<LayoutMigrationPlan> PlanAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Plans and applies. Files are renamed, so hard links to the base install survive.</summary>
    Task<LayoutMigrationResult> MigrateAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
