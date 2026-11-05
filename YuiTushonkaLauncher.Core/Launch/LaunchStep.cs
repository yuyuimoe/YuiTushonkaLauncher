namespace YuiTushonkaLauncher.Core.Launch;

/// <summary>The steps of one launch, in order. The UI shows them by name, so they are values, not log lines.</summary>
public enum LaunchStep
{
    /// <summary>Checking the instance, the files, the runtime and the profile.</summary>
    PreFlight,

    /// <summary>Checking (and applying) the SPT patch.</summary>
    PatchCheck,

    /// <summary>Starting the server, or adopting the one that is already running.</summary>
    ServerStart,

    /// <summary>Waiting for the server to answer.</summary>
    Ready,

    /// <summary>Starting the game through the instance's runtime.</summary>
    GameStart,

    /// <summary>Both processes are running and detached: the launcher's job here is done.</summary>
    Detached,
}

/// <summary>How a step ended.</summary>
public enum LaunchStepOutcome
{
    Running,

    Succeeded,

    Failed,

    /// <summary>Stopped on request, before the game started.</summary>
    Cancelled,
}

/// <summary>One event of a launch.</summary>
/// <param name="Step">Which step this is about.</param>
/// <param name="Outcome">Running while it happens, then how it ended.</param>
/// <param name="Message">A sentence meant for the user; empty while the step is running.</param>
/// <param name="Progress">0 to 1 when the step can report progress (patching does).</param>
public sealed record LaunchProgress(LaunchStep Step, LaunchStepOutcome Outcome, string Message = "", double? Progress = null);
