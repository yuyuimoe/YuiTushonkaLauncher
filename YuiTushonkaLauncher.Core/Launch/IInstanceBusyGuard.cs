namespace YuiTushonkaLauncher.Core.Launch;

/// <summary>Something of an instance is running, so a step that rewrites its files cannot proceed.</summary>
public sealed record InstanceActivity(string Kind, int ProcessId, string ExecutablePath)
{
    public override string ToString()
    {
        return string.IsNullOrEmpty(ExecutablePath) ? $"{Kind} (pid {ProcessId})" : $"{Kind} (pid {ProcessId}, {ExecutablePath})";
    }
}

/// <summary>
/// Answers "is this instance busy" for the steps that must not run next to a live process: patching, restoring,
/// exporting, migrating the layout. With detached processes this can no longer be assumed.
/// </summary>
public interface IInstanceBusyGuard
{
    /// <summary>Every running process of the instance: the server this launcher supervises, or the pair it recorded.</summary>
    Task<IReadOnlyList<InstanceActivity>> FindActivityAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Throws a message naming the process when the instance is busy.</summary>
    Task EnsureIdleAsync(Guid instanceId, string operation, CancellationToken cancellationToken = default);
}

/// <summary>The instance has a running process and the operation refuses to touch its files.</summary>
public sealed class InstanceBusyException(string message, IReadOnlyList<InstanceActivity> activity) : Exception(message)
{
    public IReadOnlyList<InstanceActivity> Activity { get; } = activity;
}
