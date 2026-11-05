using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// The list of servers, kept in the user's settings. There is always a local entry — the server the instance itself
/// starts — so the list can never be emptied into a state where nothing is selected.
/// </summary>
public sealed class ServerList : IServerList
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ServerList> _logger;
    private IReadOnlyList<ServerEntry> _all = [ServerEntry.Local];
    private ServerEntry _active = ServerEntry.Local;

    /// <summary>Creates the list over the launcher's settings.</summary>
    public ServerList(ISettingsService settings, ILogger<ServerList> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerEntry> All
    {
        get { return _all; }
    }

    /// <inheritdoc />
    public ServerEntry Active
    {
        get { return _active; }
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        var stored = settings.Servers.ToList();
        var repaired = false;

        var local = stored.FindIndex(profile => profile.IsBuiltIn);
        if (local < 0)
        {
            // A settings file from before the list existed, or one hand-edited into losing the local server.
            stored.Insert(0, ServerEntry.Local);
            repaired = true;
        }
        else if (local != 0)
        {
            var entry = stored[local];
            stored.RemoveAt(local);
            stored.Insert(0, entry);
            repaired = true;
        }

        // The local entry keeps whatever address the user gave it, but it is always the first row and always present.
        stored[0] = stored[0] with
        {
            IsBuiltIn = true,
        };

        var active =
            stored.FirstOrDefault(profile => string.Equals(profile.Name, settings.ActiveServer, StringComparison.OrdinalIgnoreCase))
            ?? stored[0];

        _all = stored;
        _active = active;

        if (repaired || settings.ActiveServer is null)
        {
            await PersistAsync(active.Name, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<ServerEntry> SaveAsync(ServerEntry profile, CancellationToken cancellationToken = default)
    {
        if (profile.ValidationError() is { } error)
        {
            throw new ServerListException(error);
        }

        var stored = _all.ToList();
        var index = stored.FindIndex(candidate => string.Equals(candidate.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

        var saved = profile with { IsBuiltIn = profile.IsBuiltIn || profile.IsLocal };
        if (index < 0)
        {
            stored.Add(saved);
        }
        else
        {
            // The built-in entry stays built in no matter what the editor sends.
            saved = saved with
            {
                IsBuiltIn = stored[index].IsBuiltIn || saved.IsBuiltIn,
            };
            stored[index] = saved;
        }

        _all = stored;
        await PersistAsync(_active.Name, cancellationToken);
        return saved;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        var stored = _all.ToList();
        var index = stored.FindIndex(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return;
        }

        if (stored[index].IsBuiltIn)
        {
            throw new ServerListException("The local server cannot be removed.");
        }

        var removed = stored[index];
        stored.RemoveAt(index);
        _all = stored;

        // Removing the active one falls back to the local server, which always exists.
        if (string.Equals(_active.Name, removed.Name, StringComparison.OrdinalIgnoreCase))
        {
            _active = stored[0];
        }

        await WriteAsync(_all, _active.Name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        var profile =
            _all.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ServerListException($"There is no server called {name}.");

        _active = profile;
        await PersistAsync(profile.Name, cancellationToken);
    }

    private async Task PersistAsync(string activeName, CancellationToken cancellationToken)
    {
        await WriteAsync(_all, activeName, cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<ServerEntry> profiles, string activeName, CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        await _settings.SaveAsync(settings with { Servers = profiles, ActiveServer = activeName }, cancellationToken);
    }
}

/// <summary>
/// Answers "is anything listening there" by opening a TCP connection. It is deliberately not an API call: a server that
/// is still starting, or one of a different SPT version, should still count as reachable.
/// </summary>
public sealed class EndpointProbe(TimeProvider time) : IEndpointProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async Task<EndpointProbeResult> ProbeAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        var started = time.GetTimestamp();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.ConnectHost, endpoint.Port, timeout.Token);
            var elapsed = (int)time.GetElapsedTime(started).TotalMilliseconds;
            return new EndpointProbeResult(true, elapsed, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EndpointProbeResult.Unreachable($"no answer within {Timeout.TotalSeconds:0} seconds");
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return EndpointProbeResult.Unreachable(exception.Message);
        }
    }
}
