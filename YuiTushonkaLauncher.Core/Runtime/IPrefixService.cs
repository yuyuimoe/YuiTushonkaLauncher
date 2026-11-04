using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>What is at a prefix path.</summary>
public enum PrefixState
{
    /// <summary>Nothing there yet (or an empty folder): <c>wineboot</c> will create it.</summary>
    Missing,

    /// <summary>A usable Wine prefix.</summary>
    Initialized,

    /// <summary>Something is there but it is not a complete prefix.</summary>
    Broken,
}

/// <summary>What a prefix path holds, with a reason when it is not usable.</summary>
/// <param name="State">Missing, usable or broken.</param>
/// <param name="Path">The inspected path.</param>
/// <param name="Reason">Why the prefix is not usable.</param>
/// <param name="LooksLikePrefix">
/// A broken folder that still has Wine's own files: <c>wineboot</c> can repair it. A folder without them is
/// somebody else's data and is never touched.
/// </param>
public sealed record PrefixInspection(PrefixState State, string Path, string? Reason = null, bool LooksLikePrefix = false);

/// <summary>One line a child process printed.</summary>
public sealed record ProcessLine(bool IsError, string Text);

/// <summary>The result of running a process to completion.</summary>
public sealed record ProcessRunResult(int ExitCode, bool TimedOut = false);

/// <summary>Runs a command to completion. The seed of the launch pipeline's process manager (phase 11).</summary>
public interface IProcessRunner
{
    /// <summary>Starts the command, forwards each output line, and waits. Cancelling kills the process tree.</summary>
    Task<ProcessRunResult> RunAsync(
        LaunchCommand command,
        IProgress<ProcessLine>? output = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The outcome of preparing a prefix.</summary>
public sealed record PrefixInitResult
{
    public required bool Succeeded { get; init; }

    public required PrefixInspection Prefix { get; init; }

    /// <summary>The command that was run; <c>null</c> when pre-flight refused to start it.</summary>
    public LaunchCommand? Command { get; init; }

    public int? ExitCode { get; init; }

    public IReadOnlyList<RuntimeProblem> Problems { get; init; } = [];
}

/// <summary>Creates, checks and opens the Wine prefix of an instance.</summary>
public interface IPrefixService
{
    /// <summary>Looks at the prefix path without changing anything.</summary>
    PrefixInspection Inspect(string prefixPath);

    /// <summary>
    /// Creates the prefix with <c>wineboot</c> when it is missing, or repairs it in place when it exists.
    /// A path that holds something that is not a prefix is refused, never wiped.
    /// </summary>
    Task<PrefixInitResult> InitializeAsync(
        Instance instance,
        RuntimeProfile profile,
        IProgress<ProcessLine>? output = null,
        RuntimeSearchOptions? search = null,
        CancellationToken cancellationToken = default
    );
}
