using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Launch;

/// <summary>
/// Finds what of an instance is running. Two sources, because there are two ways a process can be alive: the
/// supervisor knows about a server it started in this session, and the run record knows about a pair a previous
/// session left running (which is now the normal case, since nothing is killed on exit).
/// </summary>
public sealed class InstanceBusyGuard(
    ISptServerSupervisor supervisor,
    IInstanceRunStore runs,
    IProcessInspector inspector,
    IScopeService scopes
) : IInstanceBusyGuard
{
    /// <inheritdoc />
    public Task<IReadOnlyList<InstanceActivity>> FindActivityAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var activity = new List<InstanceActivity>();
        var status = supervisor.GetStatus(instanceId);

        if (
            status.State is Core.Server.ServerState.Starting or ServerState.Running or ServerState.Stopping
            && status.ProcessId is { } supervised
        )
        {
            activity.Add(new InstanceActivity("the server", supervised, string.Empty));
        }

        if (runs.Find(instanceId) is { } run)
        {
            foreach (var (kind, process) in new[] { ("the server", run.Server), ("the game", run.Game) })
            {
                if (process is not null && IsAlive(process) && activity.All(entry => entry.ProcessId != process.ProcessId))
                {
                    activity.Add(new InstanceActivity(kind, process.ProcessId, process.ExecutablePath));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<InstanceActivity>>(activity);
    }

    private bool IsAlive(RunProcess process)
    {
        return (process.Scope is { Length: > 0 } unit && scopes.IsActive(unit))
            || (process.Mechanism is RunMechanism.Setsid or RunMechanism.SystemdScope && inspector.IsGroupAlive(process.ProcessId))
            || inspector.IsAlive(process.ProcessId);
    }

    /// <inheritdoc />
    public async Task EnsureIdleAsync(Guid instanceId, string operation, CancellationToken cancellationToken = default)
    {
        var activity = await FindActivityAsync(instanceId, cancellationToken);

        if (activity.Count == 0)
        {
            return;
        }

        throw new InstanceBusyException(
            $"{operation} cannot run while {string.Join(" and ", activity.Select(entry => entry.ToString()))} is running. "
                + "Stop it first: the launcher never stops a game by itself.",
            activity
        );
    }
}
