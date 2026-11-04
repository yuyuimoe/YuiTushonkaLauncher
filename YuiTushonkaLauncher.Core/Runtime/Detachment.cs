namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>How a child process was detached from the launcher.</summary>
public enum RunMechanism
{
    /// <summary>Started as a plain child: it shares the launcher's fate.</summary>
    None,

    /// <summary><c>setsid</c>: a new session, so no death signal and no terminal signal reaches it.</summary>
    Setsid,

    /// <summary><c>systemd-run --user --scope</c>: its own cgroup, so a cgroup kill of the launcher's scope misses it.</summary>
    SystemdScope,

    /// <summary>A Windows job object the launcher does not own: the process leaves the launcher's job.</summary>
    WindowsJob,
}

/// <summary>One process the launcher started and then let go.</summary>
/// <param name="ProcessId">The pid as it was at start.</param>
/// <param name="StartedAt">When it started: with the path, this is what makes a pid safe to trust later.</param>
/// <param name="ExecutablePath">The program that was started.</param>
/// <param name="Mechanism">How it was detached, for a UI that has to explain what will outlive what.</param>
/// <param name="Port">The port the server listens on, when it is a server.</param>
/// <param name="Scope">
/// The systemd unit the process runs in, when it was detached with a scope. It is what stays identifiable after the
/// program forks: the pid of a launcher, a Proton wrapper or a shell is not the pid of the game.
/// </param>
public sealed record RunProcess(
    int ProcessId,
    DateTimeOffset StartedAt,
    string ExecutablePath,
    RunMechanism Mechanism,
    int? Port = null,
    string? Scope = null
)
{
    /// <summary>True when this process is not expected to die with the launcher.</summary>
    public bool IsDetached
    {
        get { return Mechanism != RunMechanism.None; }
    }
}

/// <summary>The processes of one instance, as the launcher left them. One record per instance, so the pair stays coherent.</summary>
public sealed record InstanceRun
{
    public required Guid InstanceId { get; init; }

    public required string InstanceName { get; init; }

    public RunProcess? Server { get; init; }

    public RunProcess? Game { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The pid of the launcher that started them: a record whose launcher is still alive is not a leftover.</summary>
    public int LauncherProcessId { get; init; }

    public InstanceRun WithServer(RunProcess? server)
    {
        return this with { Server = server, UpdatedAt = DateTimeOffset.UtcNow };
    }

    public InstanceRun WithGame(RunProcess? game)
    {
        return this with { Game = game, UpdatedAt = DateTimeOffset.UtcNow };
    }

    public bool IsEmpty
    {
        get { return Server is null && Game is null; }
    }
}

/// <summary>What a recorded process turned out to be when the launcher looked for it again.</summary>
public enum AdoptedProcessState
{
    /// <summary>Running, and its pid, start time and executable path all match the record.</summary>
    Running,

    /// <summary>Gone.</summary>
    Exited,

    /// <summary>The pid now belongs to something else: a recycled pid is never touched.</summary>
    NotOurs,
}

/// <summary>One member of a run, as it is now.</summary>
public sealed record AdoptedProcess(RunProcess Record, AdoptedProcessState State)
{
    public bool IsRunning
    {
        get { return State == AdoptedProcessState.Running; }
    }
}

/// <summary>A run found again, with what is actually alive.</summary>
public sealed record AdoptedRun(InstanceRun Run, AdoptedProcess? Server, AdoptedProcess? Game)
{
    public bool AnyRunning
    {
        get { return Server?.IsRunning == true || Game?.IsRunning == true; }
    }

    /// <summary>True when the launcher that recorded it is gone, so nobody else is watching these processes.</summary>
    public bool IsLeftover { get; init; }
}
