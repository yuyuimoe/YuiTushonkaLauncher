namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>What to start as a long-running child that outlives the launcher ([D22](../docs/plan/04-decisions.md)).</summary>
public sealed record ChildProcessStart
{
    public required LaunchCommand Command { get; init; }

    /// <summary>Receives every line of stdout and stderr as it is written.</summary>
    public Action<ProcessLine>? OnOutput { get; init; }

    /// <summary>
    /// True for a program that calls <c>GetConsoleMode</c> on its own standard output and refuses to start when the handle
    /// is a pipe instead of a console. Such a child keeps the console the spawner gives it, no handle is redirected, and
    /// <see cref="OnOutput" /> never fires; where the output goes instead is the program's own log file. Measured with SPT
    /// 4.0.9 on Windows (<c>docs/plan/03-verified-facts.md</c>).
    /// </summary>
    public bool RequiresConsole { get; init; }
}

/// <summary>A running child process.</summary>
public interface IChildProcess
{
    int ProcessId { get; }

    /// <summary>The systemd user unit the process runs in, when the scope mechanism was used.</summary>
    string? Scope { get; }

    DateTimeOffset StartedAt { get; }

    /// <summary>True from the moment the process ends. <see cref="Exited" /> completes a little later, after the last output was read.</summary>
    bool HasExited { get; }

    /// <summary>Completes with the exit code once the process is gone and its output was read.</summary>
    Task<int> Exited { get; }

    /// <summary>Asks the process to shut down cleanly (SIGTERM). On Windows there is no clean request for a console process.</summary>
    void RequestStop();

    /// <summary>Kills the process and everything it started.</summary>
    void Kill();
}

/// <summary>
/// Starts child processes and detaches them from the launcher: a scope, a session or a job of their own, so an OOM kill
/// or a crash of the launcher does not take them. The seam where that is decided, so a different policy is a different
/// implementation, not a different architecture ([D22](../docs/plan/04-decisions.md)).
/// </summary>
public interface IChildProcessSpawner
{
    /// <summary>
    /// How this machine detaches children. <see cref="RunMechanism.None" /> means they share the launcher's fate,
    /// which only happens where neither a scope nor a session can be created.
    /// </summary>
    RunMechanism Mechanism { get; }

    IChildProcess Start(ChildProcessStart request);
}

/// <summary>What a process looks like from outside: used to recognise a leftover one without trusting a pid.</summary>
public sealed record ProcessIdentity(int ProcessId, DateTimeOffset StartedAt, string? ExecutablePath);

/// <summary>Looks at and stops processes this launcher did not start in this session.</summary>
public interface IProcessInspector
{
    /// <summary>The process with this id, or <c>null</c> when none runs.</summary>
    ProcessIdentity? Find(int processId);

    bool IsAlive(int processId);

    /// <summary>
    /// True while any member of the process group is alive. A detached program is a group leader (<c>setsid</c>), so
    /// this outlives a wrapper that forked and exited, which is exactly what a launcher and Proton do.
    /// </summary>
    bool IsGroupAlive(int processGroupId);

    /// <summary>SIGTERM, then SIGKILL after <paramref name="grace" />; true when the process is gone.</summary>
    Task<bool> StopAsync(int processId, TimeSpan grace, CancellationToken cancellationToken = default);

    /// <summary>The same, for every member of a process group.</summary>
    Task<bool> StopGroupAsync(int processGroupId, TimeSpan grace, CancellationToken cancellationToken = default);
}

/// <summary>Looks at and stops systemd user units, which is how a scope-detached process is identified.</summary>
public interface IScopeService
{
    /// <summary>True when the unit exists and has a running process.</summary>
    bool IsActive(string unit);

    /// <summary>Asks every process of the unit to stop, then kills the unit after the grace period.</summary>
    Task<bool> StopAsync(string unit, TimeSpan grace, CancellationToken cancellationToken = default);
}
