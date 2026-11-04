using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher.Navigation;

/// <summary>The sections the rail offers.</summary>
public enum LauncherSection
{
    Instances,

    Forge,

    Mods,

    Server,

    Logs,

    Settings,
}

/// <summary>
/// Which instance every section works on. One place instead of each page carrying its own selection, so switching the
/// active instance in the rail also switches what the mods, server and logs pages show.
/// </summary>
public interface IInstanceContext
{
    Instance? Active { get; }

    Guid? ActiveId { get; }

    IReadOnlyList<Instance> Known { get; }

    /// <summary>Raised after <see cref="SetActiveAsync" /> or <see cref="RefreshAsync" /> changed the picture.</summary>
    event EventHandler? Changed;

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid instanceId, CancellationToken cancellationToken = default);
}

/// <summary>Reads the instance list from the service and remembers which one is active.</summary>
public sealed class InstanceContext : IInstanceContext
{
    private readonly IInstanceService _instances;
    private List<Instance> _known = [];

    public InstanceContext(IInstanceService instances)
    {
        _instances = instances;
    }

    public Instance? Active
    {
        get { return ActiveId is { } id ? _known.FirstOrDefault(instance => instance.Id == id) : _known.FirstOrDefault(); }
    }

    public Guid? ActiveId { get; private set; }

    public IReadOnlyList<Instance> Known
    {
        get { return _known; }
    }

    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _known = [.. await _instances.ListAsync(cancellationToken)];
        if (ActiveId is { } id && _known.All(instance => instance.Id != id))
        {
            ActiveId = null;
        }

        ActiveId ??= _known.FirstOrDefault()?.Id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetActiveAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        if (_known.All(instance => instance.Id != instanceId))
        {
            await RefreshAsync(cancellationToken);
        }

        if (_known.All(instance => instance.Id != instanceId))
        {
            return;
        }

        ActiveId = instanceId;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
