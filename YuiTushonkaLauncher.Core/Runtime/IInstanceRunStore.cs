namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>
/// Keeps one record per instance of the processes the launcher started and let go. It is written before a process
/// is released, so there is never a window in which a process runs and no record names it.
/// </summary>
public interface IInstanceRunStore
{
    void Save(InstanceRun run);

    void Forget(Guid instanceId);

    InstanceRun? Find(Guid instanceId);

    IReadOnlyList<InstanceRun> ReadAll();
}

/// <summary>Finds the processes of a previous session again, without ever trusting a bare pid.</summary>
public interface IRunAdoptionService
{
    /// <summary>Every recorded run whose launcher is still alive (this one, or another window of it).</summary>
    Task<IReadOnlyList<AdoptedRun>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Recorded runs left behind by a launcher that is gone. A record whose processes are all gone is forgotten;
    /// one whose pid now belongs to another program is dropped without touching that process.
    /// </summary>
    Task<IReadOnlyList<AdoptedRun>> FindLeftoversAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the running members of a run (SIGTERM, then kill after the grace period) and forgets the record.</summary>
    Task<int> StopAsync(AdoptedRun run, TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default);
}
