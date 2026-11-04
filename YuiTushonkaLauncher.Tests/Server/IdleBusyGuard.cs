using YuiTushonkaLauncher.Core.Launch;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>A guard that says nothing is running, for the services that only need it to be present.</summary>
public sealed class IdleBusyGuard : IInstanceBusyGuard
{
    public Task<IReadOnlyList<InstanceActivity>> FindActivityAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<InstanceActivity>>([]);
    }

    public Task EnsureIdleAsync(Guid instanceId, string operation, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
