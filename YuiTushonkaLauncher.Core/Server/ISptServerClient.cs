namespace YuiTushonkaLauncher.Core.Server;

/// <summary>A client mod the server recorded for a profile (<c>sptData.mods</c>), written by the game when it connects.</summary>
public sealed record SptProfileMod(string Name, string? Version, string? Author);

/// <summary>What the launcher API says about one profile.</summary>
public sealed record SptProfileSummary
{
    public required string Username { get; init; }

    public string Nickname { get; init; } = string.Empty;

    public string Side { get; init; } = string.Empty;

    public int Level { get; init; }

    public required string ProfileId { get; init; }

    public string Edition { get; init; } = string.Empty;

    /// <summary>The server could not load the profile file; it is listed but cannot be used.</summary>
    public bool InvalidOrUnloadable { get; init; }

    /// <summary>The profile has not been played yet (or a wipe is pending): the game creates the character on first launch.</summary>
    public bool PendingWipe { get; init; }

    /// <summary>The SPT version the profile was last saved with, e.g. <c>4.1.6 (40743)</c>.</summary>
    public string? SptVersion { get; init; }

    public IReadOnlyList<SptProfileMod> ClientMods { get; init; } = [];
}

/// <summary>The answer to register, remove and wipe: whether the server accepted, and the profile list afterwards.</summary>
public sealed record SptProfileChange(bool Accepted, IReadOnlyList<SptProfileSummary> Profiles);

/// <summary>A server mod the server has loaded (<c>/launcher/v2/mods</c>).</summary>
public sealed record SptServerMod
{
    public required string ModGuid { get; init; }

    public required string Name { get; init; }

    public string? Version { get; init; }

    public string? Author { get; init; }

    /// <summary>The SPT version range the mod declares, e.g. <c>~4.1.3</c>.</summary>
    public string? SptVersion { get; init; }
}

/// <summary>A mod page the server advertises (<c>/launcher/v2/mod-pages</c>).</summary>
public sealed record SptModPage(string Name, string HomePage, string? Description);

public enum SptServerFailure
{
    /// <summary>Nothing answered: refused, reset, DNS or TLS failure.</summary>
    Unreachable,

    Timeout,

    /// <summary>Something answered, but it was not the SPT launcher API (HTTP error, bad zlib, bad JSON).</summary>
    BadResponse,
}

/// <summary>The server could not be reached or did not speak the launcher API.</summary>
public sealed class SptServerException(SptServerFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public SptServerFailure Failure { get; } = failure;
}

/// <summary>The launcher API of one SPT server (<c>/launcher/v2/*</c>). Bound to one address.</summary>
public interface ISptServerClient
{
    Uri BaseUri { get; }

    /// <summary>True only when the server answers <c>Pong!</c>. Never throws for an unreachable server.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>The server's SPT version, e.g. <c>4.1.6</c>.</summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Profile editions the server offers, with their descriptions.</summary>
    Task<IReadOnlyDictionary<string, string>> GetProfileTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SptProfileSummary>> GetProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>One profile by user name, or <c>null</c> when the server has none.</summary>
    Task<SptProfileSummary?> GetProfileAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>Selects the profile on the server; false when no profile has that name.</summary>
    Task<bool> LoginAsync(string username, CancellationToken cancellationToken = default);

    Task<SptProfileChange> RegisterAsync(string username, string edition, CancellationToken cancellationToken = default);

    Task<SptProfileChange> RemoveAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>Flags a profile for a wipe that the server applies the next time the game starts.</summary>
    Task<SptProfileChange> WipeAsync(string username, string edition, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SptServerMod>> GetServerModsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SptModPage>> GetModPagesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates clients. A client trusts the server's self-signed certificate, as SPT's own launcher does.</summary>
public interface ISptServerClientFactory
{
    ISptServerClient Create(ServerEndpoint endpoint);
}
