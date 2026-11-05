namespace YuiTushonkaLauncher.Core.Server;

/// <summary>
/// A server the launcher knows about: the one that runs on this machine, and any others a user wants to keep at hand.
/// The local one is always there and cannot be removed — it is the server the instance itself starts.
/// </summary>
public sealed record ServerEntry
{
    /// <summary>How the server is called in the list. Unique, compared case-insensitively.</summary>
    public required string Name { get; init; }

    /// <summary>Host the client connects to.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>TCP port.</summary>
    public int Port { get; init; } = ServerEndpoint.DefaultPort;

    /// <summary>True for the entry the launcher always keeps: the local server, which it can start and stop.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>The address as a connectable endpoint.</summary>
    public ServerEndpoint Endpoint
    {
        get { return new ServerEndpoint(Host, Port); }
    }

    /// <summary>True when the server runs on this machine, which is what lets the launcher start and stop it.</summary>
    public bool IsLocal
    {
        get
        {
            return Host.Trim().ToLowerInvariant() switch
            {
                "" or "localhost" or "127.0.0.1" or "::1" or "[::1]" => true,
                _ => false,
            };
        }
    }

    /// <summary><c>host:port</c>, as people write it and as the settings key it.</summary>
    public string Address
    {
        get { return Endpoint.ToStringKey(); }
    }

    /// <summary>The entry that always exists: this machine, SPT's own port.</summary>
    public static ServerEntry Local
    {
        get { return new ServerEntry { Name = "Local", IsBuiltIn = true }; }
    }

    /// <summary>Why the profile cannot be saved, or <c>null</c> when it can.</summary>
    public string? ValidationError()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "A server needs a name.";
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            return "A server needs a host.";
        }

        if (Port is < 1 or > 65535)
        {
            return "A port is between 1 and 65535.";
        }

        return null;
    }

    /// <summary>Value equality, so a list of profiles compares by content.</summary>
    public bool Equals(ServerEntry? other)
    {
        return other is not null
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
            && Port == other.Port
            && IsBuiltIn == other.IsBuiltIn;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Name.ToLowerInvariant(), Host.ToLowerInvariant(), Port, IsBuiltIn);
    }
}

/// <summary>A server could not be saved, for a reason a person can act on.</summary>
public sealed class ServerListException(string message) : Exception(message);

/// <summary>The probe's answer: whether anything answered, and how quickly or why not.</summary>
/// <param name="Reachable">True when a TCP connection was made.</param>
/// <param name="Milliseconds">How long the connection took, when it happened.</param>
/// <param name="Failure">Why it did not happen, when it did not.</param>
public sealed record EndpointProbeResult(bool Reachable, int? Milliseconds, string? Failure)
{
    /// <summary>Nothing answered.</summary>
    public static EndpointProbeResult Unreachable(string failure)
    {
        return new EndpointProbeResult(false, null, failure);
    }
}

/// <summary>Asks a server address whether anything is listening, without going through the launcher API.</summary>
public interface IEndpointProbe
{
    /// <summary>A TCP connect, with a timeout short enough to sit behind a button.</summary>
    Task<EndpointProbeResult> ProbeAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default);
}

/// <summary>
/// The servers the user keeps in the launcher. There is always at least one — the local server — and one is active:
/// the address the page reports on and the one the game would be pointed at.
/// </summary>
public interface IServerList
{
    /// <summary>Every known server, the local one first.</summary>
    IReadOnlyList<ServerEntry> All { get; }

    /// <summary>The server the user is working with.</summary>
    ServerEntry Active { get; }

    /// <summary>Reads the settings, repairing them so the local server is always present.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a server, or replaces the one with the same name.</summary>
    Task<ServerEntry> SaveAsync(ServerEntry profile, CancellationToken cancellationToken = default);

    /// <summary>Removes a server by name. The local one cannot be removed.</summary>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Makes a server the active one.</summary>
    Task ActivateAsync(string name, CancellationToken cancellationToken = default);
}
