using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>Why a mod is or is not offered an update.</summary>
public enum UpdateDecision
{
    /// <summary>A newer compatible version exists.</summary>
    Update,

    /// <summary>Already on the newest compatible version.</summary>
    UpToDate,

    /// <summary>Pinned by the user; automatic updates skip it.</summary>
    Pinned,

    /// <summary>No version fits the instance's SPT version.</summary>
    Incompatible,

    /// <summary>Forge asked for the update to be held back.</summary>
    Blocked,

    /// <summary>
    /// The launcher could not confirm what the mod is (its GUID is unknown to the Forge, or could not be read at all),
    /// so it is never updated automatically: the invariant is that an unrecognised mod is left alone.
    /// </summary>
    Unrecognized,
}

/// <summary>One mod's update status.</summary>
public sealed record ModUpdateStatus
{
    public required string Guid { get; init; }

    public required string Name { get; init; }

    public required SemanticVersion InstalledVersion { get; init; }

    public SemanticVersion? AvailableVersion { get; init; }

    public UpdateDecision Decision { get; init; }

    public string? DownloadUrl { get; init; }

    public long? ContentLength { get; init; }

    /// <summary>Reason shown to the user for anything but <see cref="UpdateDecision.Update" />.</summary>
    public string? Reason { get; init; }
}

/// <summary>The update situation of one instance.</summary>
public sealed record ModUpdatePlan
{
    public required SemanticVersion SptVersion { get; init; }

    public IReadOnlyList<ModUpdateStatus> Statuses { get; init; } = [];

    public IReadOnlyList<ModUpdateStatus> Updatable
    {
        get { return [.. Statuses.Where(status => status.Decision == UpdateDecision.Update)]; }
    }

    public bool IsClean
    {
        get { return Updatable.Count == 0; }
    }
}

/// <summary>Checks installed mods against the Forge API, honouring pins and SPT compatibility.</summary>
public interface IModUpdateService
{
    /// <summary>Builds the update plan for an instance.</summary>
    Task<ModUpdatePlan> CheckAsync(Guid instanceId, bool includePinned = false, CancellationToken cancellationToken = default);
}
