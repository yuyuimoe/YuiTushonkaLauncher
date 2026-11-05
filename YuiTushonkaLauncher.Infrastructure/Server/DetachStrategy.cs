using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// A program that puts a command outside the launcher's tree. The strategy is chosen once, from what the machine
/// has, and recorded with what it started.
/// </summary>
public sealed record DetachStrategy(RunMechanism Mechanism, string? Program, IReadOnlyList<string> Arguments)
{
    /// <summary>
    /// Picks the strongest mechanism this machine offers: a systemd user scope (its own cgroup) when a user
    /// instance is running, otherwise <c>setsid</c> (a new session), otherwise nothing.
    /// </summary>
    public static DetachStrategy Choose(bool linux, string? systemdRun, bool systemdRunning, string? setsid)
    {
        if (!linux)
        {
            // Windows detaches through a job object the launcher does not own; see ChildProcessSpawner.
            return new DetachStrategy(RunMechanism.WindowsJob, null, []);
        }

        if (systemdRun is not null && systemdRunning)
        {
            return new DetachStrategy(RunMechanism.SystemdScope, systemdRun, ["--user", "--scope", "--quiet"]);
        }

        if (setsid is not null)
        {
            return new DetachStrategy(RunMechanism.Setsid, setsid, ["-w"]);
        }

        return new DetachStrategy(RunMechanism.None, null, []);
    }
}
