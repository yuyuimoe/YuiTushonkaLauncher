using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Updates;

/// <summary>A published release of the launcher itself.</summary>
public sealed record LauncherRelease
{
    /// <summary>The tag as published, e.g. <c>v0.2.0</c>.</summary>
    public required string Tag { get; init; }

    public required SemanticVersion Version { get; init; }

    /// <summary>Where a person goes to download it.</summary>
    public required string Url { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>What the check found.</summary>
public sealed record UpdateCheckResult
{
    /// <summary>The version this build reports, or <c>null</c> when the build carries no usable version.</summary>
    public string? Current { get; init; }

    /// <summary>The newest release, when the check could read one.</summary>
    public LauncherRelease? Latest { get; init; }

    /// <summary>
    /// True when there is something newer to download. A development build (a version with a pre-release suffix) never
    /// reports an update: it is ahead of the last release by construction, and telling the user to downgrade would be
    /// wrong.
    /// </summary>
    public bool HasUpdate { get; init; }

    /// <summary>Why the check produced no answer, phrased for the user; empty on success.</summary>
    public string? Reason { get; init; }

    public bool Failed
    {
        get { return Reason is { Length: > 0 }; }
    }
}

/// <summary>
/// Compares the running build against the newest GitHub release. Notify only: 1.0 never replaces itself, it shows a
/// link.
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
