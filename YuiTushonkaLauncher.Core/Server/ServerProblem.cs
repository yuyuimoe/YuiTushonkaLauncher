using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Core.Server;

/// <summary>What is wrong (or merely worth knowing) before a server starts.</summary>
public enum ServerProblemCode
{
    ExecutableMissing,
    WorkingDirectoryMissing,
    ConfigMissing,
    ConfigInvalid,
    PortInvalid,
    PortBusy,
    PortUsedByRunningInstance,
    PortConfiguredByOtherInstance,
    AlreadyRunning,
    RuntimeMissing,
    RuntimeUnverified,
    ListensOnAllInterfaces,

    /// <summary>This machine cannot detach a process, so it would die with the launcher.</summary>
    DetachUnavailable,
    RuntimeProfileInvalid,
}

/// <summary>One finding with the exact path or port it is about.</summary>
public sealed record ServerProblem(RuntimeProblemSeverity Severity, ServerProblemCode Code, string Message, string? Path = null)
{
    public bool IsError
    {
        get { return Severity == RuntimeProblemSeverity.Error; }
    }
}
