using YuiTushonkaLauncher.Core.Patching;

namespace YuiTushonkaLauncher.Patching;

/// <summary>
/// Decides, from what is on disk, what patching each file would mean. Pure: no I/O beyond stat.
/// </summary>
/// <remarks>
/// The guard is the delta's own header: a delta declares how big its input is, so a target of any
/// other size cannot be a build this delta understands. Refusing there is what keeps a mis-detected
/// install from being turned into a corrupted DLL — the official launcher simply patches and hopes.
/// </remarks>
public sealed class PatchPlanner
{
    /// <summary>Builds the plan for <paramref name="deltas" /> without writing anything.</summary>
    public PatchPlan Plan(string instanceRoot, IReadOnlyList<DeltaDescriptor> deltas)
    {
        var files = new List<PatchFilePlan>(deltas.Count);

        foreach (var delta in deltas)
        {
            files.Add(PlanFile(instanceRoot, delta));
        }

        return new PatchPlan { Files = files };
    }

    private static PatchFilePlan PlanFile(string instanceRoot, DeltaDescriptor delta)
    {
        var target = Path.Combine(instanceRoot, delta.RelativePath);
        var backup = Path.Combine(instanceRoot, delta.BackupRelativePath);

        var targetSize = SizeOf(target);
        var backupSize = SizeOf(backup);

        PatchFilePlan Plan(PatchFileAction action, string reason, string? source = null)
        {
            return new PatchFilePlan
            {
                RelativePath = delta.RelativePath,
                TargetPath = target,
                BackupPath = backup,
                Action = action,
                Reason = reason,
                ExpectedInputSize = delta.ExpectedInputSize,
                ExpectedOutputSize = delta.ExpectedOutputSize,
                CurrentSize = targetSize,
                PatchSourcePath = action == PatchFileAction.Patch ? source ?? target : null,
            };
        }

        if (targetSize is null)
        {
            return Plan(PatchFileAction.Missing, $"The file this delta patches is not in the instance: {delta.RelativePath}.");
        }

        if (targetSize == delta.ExpectedOutputSize)
        {
            return Plan(
                PatchFileAction.AlreadyPatched,
                backupSize is null
                    ? "Already the patched build; no .spt-bak exists, so restoring the original needs a fresh copy of the file."
                    : "Already the patched build."
            );
        }

        if (targetSize == delta.ExpectedInputSize)
        {
            return Plan(PatchFileAction.Patch, $"Pre-patch build ({delta.ExpectedInputSize:N0} bytes): the delta applies.");
        }

        // The target matches neither build. If a backup holds the pre-patch file, patching from it
        // repairs the install; without one there is nothing safe to do.
        if (backupSize == delta.ExpectedInputSize)
        {
            return Plan(
                PatchFileAction.Patch,
                $"The file ({targetSize:N0} bytes) is neither build; using the .spt-bak backup as the pre-patch source.",
                backup
            );
        }

        return Plan(
            PatchFileAction.Refused,
            $"The file is {targetSize:N0} bytes; these deltas patch {delta.ExpectedInputSize:N0} into {delta.ExpectedOutputSize:N0}. "
                + "It is not a build this SPT version supports: the file may be damaged or come from another game version."
        );
    }

    private static long? SizeOf(string path)
    {
        var info = new FileInfo(path);

        return info.Exists ? info.Length : null;
    }
}
