using YuiTushonkaLauncher.Core.Launch;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>
/// A guard a test can swap after the services were built: the real services take the guard once, in their
/// constructor, so a test that sets a property has to set it on an object they already hold.
/// </summary>
public sealed class SwitchableBusyGuard : IInstanceBusyGuard
{
    public IInstanceBusyGuard Current { get; set; } = new IdleBusyGuard();

    public Task<IReadOnlyList<InstanceActivity>> FindActivityAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return Current.FindActivityAsync(instanceId, cancellationToken);
    }

    public Task EnsureIdleAsync(Guid instanceId, string operation, CancellationToken cancellationToken = default)
    {
        return Current.EnsureIdleAsync(instanceId, operation, cancellationToken);
    }
}
