using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>Result of an update check, in the four buckets the API reports.</summary>
public sealed record ForgeUpdateReport
{
    public required SemanticVersion SptVersion { get; init; }

    public IReadOnlyList<ForgeUpdate> Updates { get; init; } = [];

    /// <summary>Updates that exist but are held back (the API decides when to block).</summary>
    public IReadOnlyList<ForgeUpdateTarget> BlockedUpdates { get; init; } = [];

    public IReadOnlyList<ForgeUpdateTarget> UpToDate { get; init; } = [];

    /// <summary>Installed mods that cannot be updated because no version fits the SPT version.</summary>
    public IReadOnlyList<ForgeIncompatible> Incompatible { get; init; } = [];

    /// <summary>True when nothing needs attention.</summary>
    public bool IsClean
    {
        get { return Updates.Count == 0 && BlockedUpdates.Count == 0 && Incompatible.Count == 0; }
    }
}

/// <summary>What is installed today.</summary>
public sealed record ForgeUpdateTarget
{
    public int? ModId { get; init; }

    public int? VersionId { get; init; }

    public string? Guid { get; init; }

    public required string Name { get; init; }

    public string? Slug { get; init; }

    public required SemanticVersion Version { get; init; }
}

/// <summary>The version the API recommends.</summary>
public sealed record ForgeUpdateCandidate
{
    public required int VersionId { get; init; }

    public required SemanticVersion Version { get; init; }

    public required string DownloadUrl { get; init; }

    public long? ContentLength { get; init; }

    public string? FikaCompatibility { get; init; }

    public IReadOnlyList<SemanticVersion> SupportedSptVersions { get; init; } = [];
}

/// <summary>An available update: what is installed and what is recommended.</summary>
public sealed record ForgeUpdate
{
    public required ForgeUpdateTarget Current { get; init; }

    public required ForgeUpdateCandidate Recommended { get; init; }
}

/// <summary>Why a mod cannot be updated for the selected SPT version.</summary>
public sealed record ForgeIncompatible
{
    public required ForgeUpdateTarget Current { get; init; }

    /// <summary>API reason code, e.g. <c>no_version_for_spt</c>.</summary>
    public required string Reason { get; init; }

    public SemanticVersion? LatestCompatibleVersion { get; init; }
}

/// <summary>A dependency the API reports for one package. Fields are nullable because the endpoint's
/// payload is not fully documented and varies between releases.</summary>
public sealed record ForgeDependency
{
    public int? ModId { get; init; }

    public string? Guid { get; init; }

    public string? Name { get; init; }

    public SemanticVersion? Version { get; init; }

    public bool? IsOptional { get; init; }

    /// <summary>True when the API says this dependency clashes with the asking package.</summary>
    public bool IsConflict { get; init; }

    /// <summary>Where the compatible version can be downloaded, when the API says.</summary>
    public string? DownloadUrl { get; init; }
}

/// <summary>Health of the API.</summary>
public sealed record ForgePing
{
    public required string Message { get; init; }
}
