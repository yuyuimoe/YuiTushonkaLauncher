using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>A server list held in memory, recording what the page asked it to do.</summary>
public sealed class FakeServerList : IServerList
{
    private readonly List<ServerEntry> _all = [ServerEntry.Local];
    private ServerEntry _active = ServerEntry.Local;

    /// <summary>Every name the page activated.</summary>
    public List<string> Activations { get; } = [];

    /// <summary>Every entry the page saved, in order.</summary>
    public List<ServerEntry> Saved { get; } = [];

    /// <summary>Every name the page removed.</summary>
    public List<string> Removals { get; } = [];

    public IReadOnlyList<ServerEntry> All
    {
        get { return _all; }
    }

    public ServerEntry Active
    {
        get { return _active; }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<ServerEntry> SaveAsync(ServerEntry profile, CancellationToken cancellationToken = default)
    {
        if (profile.ValidationError() is { } error)
        {
            throw new ServerListException(error);
        }

        Saved.Add(profile);
        var index = _all.FindIndex(entry => string.Equals(entry.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            _all.Add(profile);
        }
        else
        {
            _all[index] = profile;
        }

        return Task.FromResult(profile);
    }

    public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        Removals.Add(name);
        _all.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        _active = _all[0];
        return Task.CompletedTask;
    }

    public Task ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        var entry =
            _all.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ServerListException($"There is no server called {name}.");

        Activations.Add(name);
        _active = entry;
        return Task.CompletedTask;
    }
}

/// <summary>A probe that always answers the same thing.</summary>
public sealed class FakeEndpointProbe(EndpointProbeResult answer) : IEndpointProbe
{
    /// <summary>Every address the page tested.</summary>
    public List<ServerEndpoint> Asked { get; } = [];

    public Task<EndpointProbeResult> ProbeAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        Asked.Add(endpoint);
        return Task.FromResult(answer);
    }
}

/// <summary>A port service that records the port it was told to write.</summary>
public sealed class FakeServerPorts : IServerPortService
{
    /// <summary>Every (instance, port) pair the page asked for.</summary>
    public List<(Guid InstanceId, int Port)> Sets { get; } = [];

    public Task<int> SuggestFreePortAsync(int start = ServerEndpoint.DefaultPort, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(start);
    }

    public Task SetPortAsync(Guid instanceId, int port, CancellationToken cancellationToken = default)
    {
        Sets.Add((instanceId, port));
        return Task.CompletedTask;
    }
}
