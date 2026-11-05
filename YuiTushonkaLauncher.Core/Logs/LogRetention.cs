namespace YuiTushonkaLauncher.Core.Logs;

/// <summary>One rotated log file: the current name may already be compressed by the rotator.</summary>
public sealed record RotatedLog(string Path, long Bytes, DateTimeOffset Modified);

/// <summary>What a retention pass would remove, and what it compressed.</summary>
public sealed record RetentionPlan
{
    public IReadOnlyList<string> ToCompress { get; init; } = [];

    public IReadOnlyList<string> ToDelete { get; init; } = [];

    public long BytesFreed { get; init; }
}

/// <summary>
/// Keeps the launcher's own logs under control ([D17]): a rotated file is gzipped into <c>archive/</c>, and old
/// files are deleted by count and by age.
/// </summary>
public interface ILogRetention
{
    /// <summary>Reads what is on disk and says what would happen. Writes nothing.</summary>
    RetentionPlan Plan(string logsDirectory, int keepFiles, int keepDays);

    /// <summary>Compresses the rotated files and deletes what is beyond the limits; returns what it did.</summary>
    Task<RetentionPlan> ApplyAsync(string logsDirectory, int keepFiles, int keepDays, CancellationToken cancellationToken = default);
}
