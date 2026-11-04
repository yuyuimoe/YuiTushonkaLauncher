namespace YuiTushonkaLauncher.Core.Logs;

/// <summary>Which process a log line came from. The UI colors and filters by this.</summary>
public enum LogSourceKind
{
    /// <summary>The launcher itself.</summary>
    Launcher,

    /// <summary>The SPT server, which writes one file per day.</summary>
    Server,

    /// <summary>BepInEx, which is where a broken client mod says why.</summary>
    BepInEx,

    /// <summary>SPT's own per-run log folder: the game's own words, in several files.</summary>
    Game,

    /// <summary>The runtime (Proton, umu, Wine), which is what explains a failure before the game starts.</summary>
    Runtime,
}

/// <summary>One log file that belongs to an instance.</summary>
/// <param name="Kind">Which source it is.</param>
/// <param name="Path">Absolute path.</param>
/// <param name="Label">How the UI names it, e.g. <c>errors.log</c> or <c>spt20261104.log</c>.</param>
/// <param name="Bytes">Size right now.</param>
/// <param name="LastWrite">When it was last written.</param>
/// <param name="IsDefault">True for the files a reader wants first (not the 160 MiB trace log).</param>
public sealed record LogFile(LogSourceKind Kind, string Path, string Label, long Bytes, DateTimeOffset? LastWrite, bool IsDefault);

/// <summary>One line, already tagged with its source.</summary>
/// <param name="Kind">Which source it came from.</param>
/// <param name="File">The file's name, for a view that mixes several.</param>
/// <param name="Number">
/// The line's position in the returned tail, starting at 1. Not the line number in the file: the reader only looks at
/// the end of it, and counting from the start of a 160 MiB file would mean reading the whole thing.
/// </param>
/// <param name="Text">The line.</param>
public sealed record LogLine(LogSourceKind Kind, string File, int Number, string Text);

/// <summary>The largest file the panel follows. Above it, the file belongs in a text editor.</summary>
public static class LogLimits
{
    /// <summary>64 MiB: the game's own <c>errors.log</c> and <c>traces.log</c> pass it in one session (measured: 81 and 163 MiB).</summary>
    public const long MaxFollowedBytes = 64L * 1024 * 1024;

    /// <summary>How much of a file the reader looks at from the end when it only needs the tail.</summary>
    public const int DefaultTailLines = 200;
}

/// <summary>The result of reading a log: the newest lines, and whether the file was too large to follow.</summary>
public sealed record LogTail
{
    public required LogFile File { get; init; }

    public IReadOnlyList<LogLine> Lines { get; init; } = [];

    /// <summary>True when the file is larger than <see cref="LogLimits.MaxFollowedBytes" /> and was not read.</summary>
    public bool TooLarge { get; init; }

    public string? Reason { get; init; }

    /// <summary>The file the lines came from is missing or unreadable.</summary>
    public bool Missing { get; init; }
}

/// <summary>Finds the log files of an instance, newest first, tagged by source.</summary>
public interface ILogCatalog
{
    Task<IReadOnlyList<LogFile>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>The files of one kind, newest first.</summary>
    Task<IReadOnlyList<LogFile>> ListAsync(Guid instanceId, LogSourceKind kind, CancellationToken cancellationToken = default);
}

/// <summary>Reads the end of a log file without loading the whole thing.</summary>
public interface ILogReader
{
    Task<LogTail> ReadTailAsync(LogFile file, int maxLines = LogLimits.DefaultTailLines, CancellationToken cancellationToken = default);
}

/// <summary>Lists, tails and exports the logs of an instance.</summary>
public interface ILogService
{
    Task<IReadOnlyList<LogFile>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task<LogTail> ReadTailAsync(LogFile file, int maxLines = LogLimits.DefaultTailLines, CancellationToken cancellationToken = default);

    /// <summary>Writes the given lines as text (no re-formatting beyond the source tag).</summary>
    Task<string> ExportLinesAsync(IReadOnlyList<LogLine> lines, string destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the crash bundle of an instance: versions, instance state, the last lines of every default source and
    /// the hashes of the files a patch or a mod is about.
    /// </summary>
    Task<string> ExportDiagnosticsAsync(Guid instanceId, string destination, CancellationToken cancellationToken = default);
}
