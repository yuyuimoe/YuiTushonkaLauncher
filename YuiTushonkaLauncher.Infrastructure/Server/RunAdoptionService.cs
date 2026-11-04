using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Recognises the processes of a previous session. A pid alone is never enough: it must be alive, have started at
/// the recorded time (within a couple of seconds) and be the program that was recorded, or it is somebody else.
/// </summary>
public sealed class RunAdoptionService(
    IInstanceRunStore store,
    IProcessInspector inspector,
    IProcessInspector launchers,
    IScopeService scopes,
    ILogger<RunAdoptionService> logger
) : IRunAdoptionService
{
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public Task<IReadOnlyList<AdoptedRun>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AdoptedRun>>([
            .. store.ReadAll().Select(run => Adopt(run, leftover: false)).Where(adopted => adopted.AnyRunning),
        ]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AdoptedRun>> FindLeftoversAsync(CancellationToken cancellationToken = default)
    {
        var leftovers = new List<AdoptedRun>();

        foreach (var run in store.ReadAll())
        {
            var adopted = Adopt(run, leftover: true);

            if (!adopted.AnyRunning)
            {
                // Nothing of it is alive: the record has done its job.
                store.Forget(run.InstanceId);
                continue;
            }

            // Another launcher that is still alive owns these processes.
            if (launchers.IsAlive(run.LauncherProcessId))
            {
                continue;
            }

            leftovers.Add(adopted);
        }

        return Task.FromResult<IReadOnlyList<AdoptedRun>>(leftovers);
    }

    /// <inheritdoc />
    public async Task<int> StopAsync(AdoptedRun run, TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default)
    {
        var stopped = 0;

        foreach (var member in new[] { run.Game, run.Server })
        {
            if (member is not { State: AdoptedProcessState.Running })
            {
                continue;
            }

            if (await StopMemberAsync(member.Record, gracePeriod ?? DefaultGrace, cancellationToken))
            {
                stopped++;
            }
            else
            {
                logger.LogWarning(
                    "The process {Process} ({Path}) did not stop; the record is kept",
                    member.Record.ProcessId,
                    member.Record.ExecutablePath
                );
                return stopped;
            }
        }

        store.Forget(run.Run.InstanceId);
        return stopped;
    }

    /// <summary>
    /// Stops a member the way it was detached: a scope by its unit, a session by its process group, anything else by
    /// pid. A wrapper that forked and exited would make a pid-only stop leave the game running.
    /// </summary>
    private async Task<bool> StopMemberAsync(RunProcess record, TimeSpan grace, CancellationToken cancellationToken)
    {
        if (record.Scope is { Length: > 0 } unit && scopes.IsActive(unit))
        {
            return await scopes.StopAsync(unit, grace, cancellationToken);
        }

        if (record.Mechanism is RunMechanism.Setsid or RunMechanism.SystemdScope && inspector.IsGroupAlive(record.ProcessId))
        {
            return await inspector.StopGroupAsync(record.ProcessId, grace, cancellationToken);
        }

        return await inspector.StopAsync(record.ProcessId, grace, cancellationToken);
    }

    private AdoptedRun Adopt(InstanceRun run, bool leftover)
    {
        return new AdoptedRun(run, Check(run.Server), Check(run.Game)) { IsLeftover = leftover };
    }

    private AdoptedProcess? Check(RunProcess? record)
    {
        if (record is null)
        {
            return null;
        }

        // The scope is the durable identity: the recorded pid can be a wrapper that forked the real program and left.
        if (record.Scope is { Length: > 0 } unit && scopes.IsActive(unit))
        {
            return new AdoptedProcess(record, AdoptedProcessState.Running);
        }

        if (record.Mechanism is RunMechanism.Setsid or RunMechanism.SystemdScope && inspector.IsGroupAlive(record.ProcessId))
        {
            return new AdoptedProcess(record, AdoptedProcessState.Running);
        }

        var identity = inspector.Find(record.ProcessId);

        if (identity is null)
        {
            return new AdoptedProcess(record, AdoptedProcessState.Exited);
        }

        var sameStart = Math.Abs((identity.StartedAt - record.StartedAt).TotalSeconds) < 2;
        var sameProgram =
            string.IsNullOrEmpty(record.ExecutablePath)
            || identity.ExecutablePath is null
            || string.Equals(identity.ExecutablePath, record.ExecutablePath, StringComparison.Ordinal);

        return sameStart && sameProgram
            ? new AdoptedProcess(record, AdoptedProcessState.Running)
            : new AdoptedProcess(record, AdoptedProcessState.NotOurs);
    }
}
