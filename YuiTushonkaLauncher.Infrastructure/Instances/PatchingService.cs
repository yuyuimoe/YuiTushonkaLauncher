using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Patching;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Applies, verifies and reverts the SPT deltas of an instance.
/// </summary>
/// <remarks>
/// <para>
/// The order is always: plan from what is on disk, back up the original, patch into a temporary file,
/// verify the temporary, and only then rename it over the target. Nothing writes through a hard link,
/// and a failed verification leaves the original untouched (D6, D8).
/// </para>
/// <para>
/// The pre-patch guard is the delta header: a delta states how large its input must be, so a target of
/// another size is refused instead of turned into a corrupted DLL.
/// </para>
/// </remarks>
public sealed class PatchingService(
    IUnitOfWork unitOfWork,
    IPatchEngine engine,
    DeltaSetReader deltaSetReader,
    PatchPlanner planner,
    BackupScanner backupScanner,
    ILinkAwareFileWriter fileWriter,
    ILogger<PatchingService> logger
) : IPatchService
{
    /// <inheritdoc />
    public async Task<PatchPlan> PlanAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await FindAsync(instanceId, cancellationToken);
        var read = ReadDeltas(instance);

        foreach (var warning in read.Warnings)
        {
            logger.LogWarning("{Warning}", warning);
        }

        var plan = planner.Plan(instance.Path, read.Deltas);
        logger.LogInformation("Patch plan for {Instance}: {Summary}", instance.Name, plan.Summary);

        return plan;
    }

    /// <inheritdoc />
    public async Task<PatchReport> PatchAsync(
        Guid instanceId,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var instance = await FindAsync(instanceId, cancellationToken);
        var read = ReadDeltas(instance);

        foreach (var warning in read.Warnings)
        {
            logger.LogWarning("{Warning}", warning);
        }

        var plan = planner.Plan(instance.Path, read.Deltas);
        var deltas = read.Deltas.ToDictionary(delta => delta.RelativePath, StringComparer.OrdinalIgnoreCase);
        var results = new List<PatchFileResult>(plan.Files.Count);

        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            results.Add(await ApplyOneAsync(file, deltas[file.RelativePath], progress, cancellationToken));
        }

        var report = new PatchReport { Files = results };
        logger.LogInformation("Patching {Instance}: {Summary}", instance.Name, report.Summary);

        await PersistAsync(instance.Id, plan, results, cancellationToken);

        return report;
    }

    /// <inheritdoc />
    public async Task<PatchReport> RestoreAsync(
        Guid instanceId,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var instance = await FindAsync(instanceId, cancellationToken);
        var pairs = backupScanner.Discover(instance.Path);
        var results = new List<PatchFileResult>(pairs.Count);

        for (var index = 0; index < pairs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pair = pairs[index];
            progress?.Report(
                new PatchProgress
                {
                    FileName = pair.RelativePath,
                    Percentage = 100.0 * index / pairs.Count,
                    BytesWritten = index,
                    TotalBytes = pairs.Count,
                }
            );

            results.Add(await RestoreOne(pair, cancellationToken));
        }

        var report = new PatchReport { Files = results };
        logger.LogInformation("Restoring {Instance}: {Summary}", instance.Name, report.Summary);

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.Instances.SingleAsync(entity => entity.Id == instance.Id, token);
                row.Patch.IsPatched = false;
                row.Patch.PatchedSha256 = null;
                row.Patch.BackupPath = null;
                row.Patch.AppliedAt = null;
            },
            cancellationToken
        );

        return report;
    }

    private async Task<PatchFileResult> ApplyOneAsync(
        PatchFilePlan file,
        DeltaDescriptor delta,
        IProgress<PatchProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        switch (file.Action)
        {
            case PatchFileAction.AlreadyPatched:
                return new PatchFileResult
                {
                    RelativePath = file.RelativePath,
                    Outcome = PatchFileOutcome.AlreadyPatched,
                    Message = file.Reason,
                    SizeAfter = file.CurrentSize,
                    Sha256After = FileHash.TryCompute(file.TargetPath),
                };
            case PatchFileAction.Missing:
                return new PatchFileResult
                {
                    RelativePath = file.RelativePath,
                    Outcome = PatchFileOutcome.Failed,
                    Message = file.Reason,
                };
            case PatchFileAction.Refused:
                return new PatchFileResult
                {
                    RelativePath = file.RelativePath,
                    Outcome = PatchFileOutcome.Refused,
                    Message = file.Reason,
                    SizeBefore = file.CurrentSize,
                };
        }

        var beforeSize = file.CurrentSize;
        var beforeHash = FileHash.TryCompute(file.TargetPath);

        try
        {
            await EnsureBackupAsync(file, cancellationToken);

            var source = file.PatchSourcePath ?? file.TargetPath;
            var temporary = file.TargetPath + ".spt-patch";
            AtomicWrite.Discard(temporary);

            try
            {
                engine.Apply(delta.DeltaPath, source, temporary, progress, cancellationToken);

                var temporarySize = new FileInfo(temporary).Length;
                if (temporarySize != delta.ExpectedOutputSize)
                {
                    throw new InvalidDataException(
                        $"The patch produced {temporarySize:N0} bytes, but the delta declares {delta.ExpectedOutputSize:N0}. Nothing was replaced."
                    );
                }

                var afterHash = await AtomicWrite.CommitAsync(temporary, file.TargetPath, cancellationToken);

                logger.LogInformation("Patched {File} ({Before} -> {After} bytes)", file.RelativePath, beforeSize, temporarySize);

                return new PatchFileResult
                {
                    RelativePath = file.RelativePath,
                    Outcome = PatchFileOutcome.Patched,
                    Message = file.Reason,
                    SizeBefore = beforeSize,
                    SizeAfter = temporarySize,
                    Sha256Before = beforeHash,
                    Sha256After = afterHash,
                };
            }
            finally
            {
                AtomicWrite.Discard(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Patching {File} failed: {Message}", file.RelativePath, exception.Message);

            return new PatchFileResult
            {
                RelativePath = file.RelativePath,
                Outcome = PatchFileOutcome.Failed,
                Message = exception.Message,
                SizeBefore = beforeSize,
                Sha256Before = beforeHash,
                Sha256After = FileHash.TryCompute(file.TargetPath),
            };
        }
    }

    private async Task EnsureBackupAsync(PatchFilePlan file, CancellationToken cancellationToken)
    {
        if (File.Exists(file.BackupPath))
        {
            logger.LogDebug("Keeping the existing backup of {File}", file.RelativePath);

            return;
        }

        var source = file.PatchSourcePath ?? file.TargetPath;

        // CopyFileAsync detaches a shared destination first, so a backup can never become a link that
        // drags a later restore into the base install.
        await fileWriter.CopyFileAsync(source, file.BackupPath, overwrite: false, cancellationToken);
        logger.LogInformation("Backed up {File} to {Backup}", file.RelativePath, DeltaSetLocation.BackupSuffix);
    }

    private async Task<PatchFileResult> RestoreOne(BackupPair pair, CancellationToken cancellationToken)
    {
        if (!File.Exists(pair.TargetPath))
        {
            return new PatchFileResult
            {
                RelativePath = pair.RelativePath,
                Outcome = PatchFileOutcome.Skipped,
                Message = "The file this backup belongs to is gone; the backup was kept.",
            };
        }

        var backupHash = FileHash.TryCompute(pair.BackupPath);
        var targetHash = FileHash.TryCompute(pair.TargetPath);

        if (string.Equals(backupHash, targetHash, StringComparison.OrdinalIgnoreCase))
        {
            return new PatchFileResult
            {
                RelativePath = pair.RelativePath,
                Outcome = PatchFileOutcome.AlreadyPatched,
                Message = "The file already matches its backup; the backup was kept.",
                SizeAfter = new FileInfo(pair.TargetPath).Length,
                Sha256After = targetHash,
            };
        }

        try
        {
            var restored = await BackupScanner.RestoreAsync(pair, cancellationToken);

            logger.LogInformation("Restored {File} from its backup", pair.RelativePath);

            return new PatchFileResult
            {
                RelativePath = pair.RelativePath,
                Outcome = PatchFileOutcome.Restored,
                Message = "Restored the original file and removed the backup.",
                SizeAfter = new FileInfo(pair.TargetPath).Length,
                Sha256Before = targetHash,
                Sha256After = restored,
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Restoring {File} failed", pair.RelativePath);

            return new PatchFileResult
            {
                RelativePath = pair.RelativePath,
                Outcome = PatchFileOutcome.Failed,
                Message = exception.Message,
                Sha256Before = targetHash,
            };
        }
    }

    private async Task PersistAsync(Guid instanceId, PatchPlan plan, List<PatchFileResult> results, CancellationToken cancellationToken)
    {
        // Without deltas there is nothing to record, and overwriting would erase what an earlier run
        // (or the adoption path) already stored.
        if (plan.Files.Count == 0)
        {
            return;
        }

        var primary =
            plan.Files.FirstOrDefault(file => file.RelativePath.EndsWith("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
            ?? plan.Files.FirstOrDefault();

        var applied = results.FirstOrDefault(result =>
            primary is not null
            && string.Equals(result.RelativePath, primary.RelativePath, StringComparison.OrdinalIgnoreCase)
            && result.Outcome is PatchFileOutcome.Patched or PatchFileOutcome.AlreadyPatched
        );

        var patched =
            results.Count > 0
            && results.All(result =>
                result.Outcome is PatchFileOutcome.Patched or PatchFileOutcome.AlreadyPatched or PatchFileOutcome.Skipped
            );

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.Instances.SingleAsync(entity => entity.Id == instanceId, token);
                row.Patch.IsPatched = patched;
                row.Patch.OriginalSha256 = primary is null ? null : FileHash.TryCompute(primary.BackupPath);
                row.Patch.PatchedSha256 = applied?.Sha256After;
                row.Patch.BackupPath = primary is not null && File.Exists(primary.BackupPath) ? primary.BackupPath : null;
                row.Patch.AppliedAt = patched ? DateTimeOffset.UtcNow : null;
            },
            cancellationToken
        );
    }

    private DeltaSetReadResult ReadDeltas(Instance instance)
    {
        var patches = DeltaSetLocation.Resolve(instance.Path, instance.Layout);

        return deltaSetReader.Read(instance.Path, patches);
    }

    private async Task<Instance> FindAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var instance = await unitOfWork.ExecuteAsync(
            async (context, token) => await context.Instances.SingleOrDefaultAsync(row => row.Id == instanceId, token),
            cancellationToken
        );

        if (instance is null)
        {
            throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
        }

        return instance;
    }
}
