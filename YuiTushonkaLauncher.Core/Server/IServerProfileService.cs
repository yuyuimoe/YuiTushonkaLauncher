namespace YuiTushonkaLauncher.Core.Server;

/// <summary>The server is not running (or not answering), so a profile operation cannot happen.</summary>
public sealed class ServerNotRunningException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A profile operation was refused, for a reason a person can act on.</summary>
public sealed class ProfileOperationException(string message) : Exception(message);

/// <summary>The result of a destructive profile operation.</summary>
public sealed record ProfileOperationResult
{
    public required bool Accepted { get; init; }

    public required IReadOnlyList<SptProfileSummary> Profiles { get; init; }

    /// <summary>The copy of the profile file taken before the operation; <c>null</c> when the profile had no file yet.</summary>
    public string? BackupPath { get; init; }
}

/// <summary>
/// Manages the profiles of an instance's running server. The server owns the files: everything goes through
/// its API, and the only thing this service writes is a backup copy.
/// </summary>
public interface IServerProfileService
{
    Task<IReadOnlyList<SptProfileSummary>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Creates a profile. The server accepts any text, so the name and the edition are checked here.</summary>
    Task<IReadOnlyList<SptProfileSummary>> CreateAsync(
        Guid instanceId,
        string username,
        string edition,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes a profile after copying its file. <paramref name="confirmed" /> must be true.</summary>
    Task<ProfileOperationResult> DeleteAsync(
        Guid instanceId,
        string username,
        bool confirmed,
        CancellationToken cancellationToken = default
    );

    /// <summary>Wipes a profile after copying its file. <paramref name="confirmed" /> must be true.</summary>
    Task<ProfileOperationResult> WipeAsync(Guid instanceId, string username, bool confirmed, CancellationToken cancellationToken = default);

    /// <summary>Selects a profile on the server and remembers it as the last one used by this instance.</summary>
    Task<SptProfileSummary> SelectAsync(Guid instanceId, string username, CancellationToken cancellationToken = default);

    /// <summary>The profile this instance used last, if it still exists on the server.</summary>
    Task<SptProfileSummary?> GetLastUsedAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
