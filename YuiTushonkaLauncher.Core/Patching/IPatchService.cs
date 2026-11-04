namespace YuiTushonkaLauncher.Core.Patching;

/// <summary>What patching would do to one file.</summary>
public enum PatchFileAction
{
    /// <summary>The delta can be applied: the file is the pre-patch build.</summary>
    Patch,

    /// <summary>A backup exists and the file is already the patched build: nothing to do.</summary>
    AlreadyPatched,

    /// <summary>The delta cannot be applied safely; the reason says why.</summary>
    Refused,

    /// <summary>The file the delta patches is not in this instance.</summary>
    Missing,
}

/// <summary>The plan for one delta.</summary>
public sealed record PatchFilePlan
{
    public required string RelativePath { get; init; }

    public required string TargetPath { get; init; }

    public required string BackupPath { get; init; }

    public required PatchFileAction Action { get; init; }

    /// <summary>Why this action was chosen, shown to the user as-is.</summary>
    public required string Reason { get; init; }

    public required long ExpectedInputSize { get; init; }

    public required long ExpectedOutputSize { get; init; }

    /// <summary>Size of the file on disk, when it exists.</summary>
    public long? CurrentSize { get; init; }

    /// <summary>The source the patch will read: the backup when the target was already rewritten.</summary>
    public string? PatchSourcePath { get; init; }
}

/// <summary>Everything needed to decide whether to patch, and what that would change.</summary>
public sealed record PatchPlan
{
    public IReadOnlyList<PatchFilePlan> Files { get; init; } = [];

    /// <summary>True when at least one file can be patched and none is refused.</summary>
    public bool CanApply
    {
        get
        {
            return Files.Any(file => file.Action == PatchFileAction.Patch) && !Files.Any(file => file.Action == PatchFileAction.Refused);
        }
    }

    /// <summary>True when every delta is already applied.</summary>
    public bool IsFullyPatched
    {
        get { return Files.Count > 0 && Files.All(file => file.Action == PatchFileAction.AlreadyPatched); }
    }

    /// <summary>One-line description of the plan, suitable for a log line or a status bar.</summary>
    public string Summary
    {
        get
        {
            if (Files.Count == 0)
            {
                return "No deltas were found: this instance has nothing to patch.";
            }

            var patch = Files.Count(file => file.Action == PatchFileAction.Patch);
            var patched = Files.Count(file => file.Action == PatchFileAction.AlreadyPatched);
            var refused = Files.Count(file => file.Action == PatchFileAction.Refused);
            var missing = Files.Count(file => file.Action == PatchFileAction.Missing);

            return $"{patch} to patch, {patched} already patched, {refused} refused, {missing} missing.";
        }
    }
}

/// <summary>Outcome of attempting one file.</summary>
public enum PatchFileOutcome
{
    Patched,

    AlreadyPatched,

    Restored,

    Skipped,

    Refused,

    Failed,
}

/// <summary>What happened to one file, with the hashes that prove it.</summary>
public sealed record PatchFileResult
{
    public required string RelativePath { get; init; }

    public required PatchFileOutcome Outcome { get; init; }

    public required string Message { get; init; }

    public long? SizeBefore { get; init; }

    public long? SizeAfter { get; init; }

    public string? Sha256Before { get; init; }

    public string? Sha256After { get; init; }
}

/// <summary>Result of a patch or restore run.</summary>
public sealed record PatchReport
{
    public IReadOnlyList<PatchFileResult> Files { get; init; } = [];

    /// <summary>True when no file failed or was refused.</summary>
    public bool Succeeded
    {
        get
        {
            return Files.All(file =>
                file.Outcome
                    is PatchFileOutcome.Patched
                        or PatchFileOutcome.AlreadyPatched
                        or PatchFileOutcome.Restored
                        or PatchFileOutcome.Skipped
            );
        }
    }

    public string Summary
    {
        get
        {
            if (Files.Count == 0)
            {
                return "Nothing to do.";
            }

            var patched = Files.Count(file => file.Outcome == PatchFileOutcome.Patched);
            var restored = Files.Count(file => file.Outcome == PatchFileOutcome.Restored);
            var already = Files.Count(file => file.Outcome == PatchFileOutcome.AlreadyPatched);
            var failed = Files.Count(file => file.Outcome is PatchFileOutcome.Failed or PatchFileOutcome.Refused);

            return $"{patched} patched, {restored} restored, {already} unchanged, {failed} failed or refused.";
        }
    }
}

/// <summary>Applies, verifies and reverts the SPT deltas of an instance.</summary>
public interface IPatchService
{
    /// <summary>Works out what patching would do, without touching a byte.</summary>
    Task<PatchPlan> PlanAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Applies the deltas, backing up every target first and verifying the result.</summary>
    Task<PatchReport> PatchAsync(Guid instanceId, IProgress<PatchProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Restores every <c>.spt-bak</c> in the instance over its target.</summary>
    Task<PatchReport> RestoreAsync(
        Guid instanceId,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
