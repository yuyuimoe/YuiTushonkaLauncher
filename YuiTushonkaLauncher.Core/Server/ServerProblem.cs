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
    DeathSignalUnavailable,
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
