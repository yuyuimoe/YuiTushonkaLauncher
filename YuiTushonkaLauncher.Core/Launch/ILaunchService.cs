using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Core.Launch;

/// <summary>What to launch and with which profile.</summary>
public sealed record LaunchRequest
{
    public required Guid InstanceId { get; init; }

    /// <summary>
    /// The profile to log into, by name. Empty means "whatever this instance remembers"; when nothing is remembered
    /// the pre-flight refuses and says so, because guessing would open the game on the wrong profile.
    /// </summary>
    public string? ProfileUsername { get; init; }

    /// <summary>Remember the chosen profile for this server, so the next launch does not have to ask.</summary>
    public bool RememberProfile { get; init; }

    /// <summary>Skip the patch step (a diagnostic: an unpatched binary will not work).</summary>
    public bool SkipPatch { get; init; }

    /// <summary>
    /// Run this instead of the instance's game executable: it proves the pipeline on a machine that cannot run
    /// the game without pretending the game ran.
    /// </summary>
    public string? GameProgramOverride { get; init; }

    public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];

    /// <summary>
    /// The server the game should talk to. <c>null</c> means the instance's own server, which is what a launch from the
    /// instance does; a remote entry is chosen in the server picker and only changes the address the game is given.
    /// </summary>
    public ServerEndpoint? ServerEndpoint { get; init; }
}

/// <summary>Why a launch cannot proceed.</summary>
public enum LaunchProblemCode
{
    InstanceMissing,
    NotMerged,
    FilesMissing,
    PatchFailed,
    RuntimeUnavailable,
    ProfileMissing,
    ProfileUnknown,
    ProfileUnloadable,
    ServerFailed,
    GameFailed,
    AlreadyRunning,
    Busy,
}

/// <summary>One reason a launch cannot proceed, with what it is about.</summary>
public sealed record LaunchProblem(LaunchProblemCode Code, string Message, string? Path = null);

/// <summary>The pre-flight: everything checked, nothing started.</summary>
public sealed record LaunchPreflight
{
    public required Guid InstanceId { get; init; }

    public required string InstanceName { get; init; }

    public IReadOnlyList<LaunchProblem> Problems { get; init; } = [];

    /// <summary>The profile that would be used, when one was found.</summary>
    public string? ProfileUsername { get; init; }

    /// <summary>How the game would start, when the runtime could plan it.</summary>
    public LaunchCommand? GameCommand { get; init; }

    /// <summary>True when the patch step would be a no-op.</summary>
    public bool PatchAlreadyApplied { get; init; }

    /// <summary>The server is already running and would be adopted instead of started.</summary>
    public bool ServerAlreadyRunning { get; init; }

    public bool CanLaunch
    {
        get { return Problems.Count == 0 && GameCommand is not null; }
    }
}

/// <summary>What a launch ended up doing.</summary>
public sealed record LaunchResult
{
    public required Guid InstanceId { get; init; }

    public required bool Succeeded { get; init; }

    public int? ServerProcessId { get; init; }

    public int? GameProcessId { get; init; }

    /// <summary>The profile the game was told to use (the token of the command line).</summary>
    public string? ProfileId { get; init; }

    public string? GameCommand { get; init; }

    public IReadOnlyList<LaunchProblem> Problems { get; init; } = [];

    public TimeSpan? Took { get; init; }
}

/// <summary>Raised when a launch cannot start; the message is meant to be shown as it is.</summary>
public sealed class LaunchException(string message, IReadOnlyList<LaunchProblem>? problems = null) : Exception(message)
{
    public IReadOnlyList<LaunchProblem> Problems { get; } = problems ?? [];
}

/// <summary>The single launch pipeline: verify → patch if needed → server → game, detached.</summary>
public interface ILaunchService
{
    /// <summary>Checks everything and starts nothing.</summary>
    Task<LaunchPreflight> PlanAsync(LaunchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the pipeline and reports every step, returning once the game is detached. Cancelling is honoured until
    /// the game starts; after that the game is detached and stopping it is an explicit Stop.
    /// </summary>
    Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
