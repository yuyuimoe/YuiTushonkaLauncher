namespace YuiTushonkaLauncher.Core.Server;

/// <summary>How an installed mod compares with the server's.</summary>
public enum ServerSyncStatus
{
    InSync,

    /// <summary>The server runs it and the instance does not have it.</summary>
    Missing,

    /// <summary>Both have it, at different versions.</summary>
    VersionDiffers,

    /// <summary>Different versions, but the installed one is pinned, so it is left alone unless asked.</summary>
    PinnedDiffers,

    /// <summary>The instance has it and the server does not run it.</summary>
    LocalOnly,
}

public sealed record ServerSyncEntry
{
    public required string Guid { get; init; }

    public required string Name { get; init; }

    public string? ServerVersion { get; init; }

    public string? LocalVersion { get; init; }

    public required ServerSyncStatus Status { get; init; }

    public string? Detail { get; init; }
}

/// <summary>A client mod the server recorded for the selected profile, compared with the ones installed.</summary>
public sealed record ClientModEntry
{
    public required string Name { get; init; }

    public string? RecordedVersion { get; init; }

    public string? LocalVersion { get; init; }

    public required ServerSyncStatus Status { get; init; }
}

/// <summary>The difference between what a server runs and what an instance has installed.</summary>
public sealed record ServerSyncPlan
{
    public required ServerEndpoint Server { get; init; }

    public string? ServerSptVersion { get; init; }

    /// <summary>Server mods against the instance's server mods; this is the part that can be applied.</summary>
    public IReadOnlyList<ServerSyncEntry> ServerMods { get; init; } = [];

    /// <summary>
    /// The profile's client mods as the server last recorded them, against the instance's client mods.
    /// Informational: the server only learns them from the game, so they are empty until the profile is played.
    /// </summary>
    public IReadOnlyList<ClientModEntry> ClientMods { get; init; } = [];

    public IReadOnlyList<ServerSyncEntry> Actionable
    {
        get { return [.. ServerMods.Where(entry => entry.Status is ServerSyncStatus.Missing or ServerSyncStatus.VersionDiffers)]; }
    }

    public bool IsInSync
    {
        get { return ServerMods.All(entry => entry.Status is ServerSyncStatus.InSync or ServerSyncStatus.LocalOnly); }
    }
}

public sealed record ServerSyncOptions
{
    /// <summary>Also change a mod the user pinned. Off by default: a pin is a promise.</summary>
    public bool OverridePins { get; init; }
}

public sealed record ServerSyncOutcome(ServerSyncEntry Entry, bool Applied, string? Error);

public sealed record ServerSyncResult(IReadOnlyList<ServerSyncOutcome> Outcomes)
{
    public bool Succeeded
    {
        get { return Outcomes.All(outcome => outcome.Applied); }
    }
}

/// <summary>Compares an instance with a server's mod list and brings the instance in line with it.</summary>
public interface IServerSyncService
{
    /// <summary>
    /// Compares the instance with <paramref name="server" /> (the instance's own server when <c>null</c>).
    /// Nothing is changed.
    /// </summary>
    Task<ServerSyncPlan> PlanAsync(Guid instanceId, ServerEndpoint? server = null, CancellationToken cancellationToken = default);

    /// <summary>Installs the exact versions the server runs, from the Forge. Pinned mods are skipped unless the options say otherwise.</summary>
    Task<ServerSyncResult> ApplyAsync(
        Guid instanceId,
        ServerSyncPlan plan,
        ServerSyncOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
