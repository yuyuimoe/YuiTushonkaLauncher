using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>A mod as the Forge API describes it. Mirrors the wire fields the launcher uses, nothing more.</summary>
public sealed record ForgeMod
{
    public required int Id { get; init; }

    public int? HubId { get; init; }

    /// <summary>Canonical GUID; <c>null</c> for catalogue entries that have none yet.</summary>
    public string? Guid { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? Teaser { get; init; }

    public string? Description { get; init; }

    public string? ThumbnailUrl { get; init; }

    public string? Author { get; init; }

    public int Downloads { get; init; }

    public int Favourites { get; init; }

    public int? CategoryId { get; init; }

    public bool IsFeatured { get; init; }

    /// <summary>Fika multiplayer compatibility, as published at mod level.</summary>
    public bool FikaCompatible { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Only populated when the request asked for <c>include=versions</c> (latest six).</summary>
    public IReadOnlyList<ForgeModVersion> Versions { get; init; } = [];
}

/// <summary>One published version of a <see cref="ForgeMod" />.</summary>
public sealed record ForgeModVersion
{
    public required int Id { get; init; }

    public required SemanticVersion Version { get; init; }

    /// <summary>Constraint text as published (may contain stray whitespace).</summary>
    public required VersionRange SptConstraint { get; init; }

    /// <summary>Concrete SPT versions the API resolved from the constraint, when it sent them.</summary>
    public IReadOnlyList<SemanticVersion> SupportedSptVersions { get; init; } = [];

    public required string DownloadUrl { get; init; }

    public long? ContentLength { get; init; }

    public int Downloads { get; init; }

    public string? FikaCompatibility { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>An addon attached to a mod. Addons constrain the <em>mod</em> version, not the SPT version.</summary>
public sealed record ForgeAddon
{
    public required int Id { get; init; }

    /// <summary>Forge id of the parent mod.</summary>
    public required int ModId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public bool IsDetached { get; init; }

    public string? Teaser { get; init; }

    public string? ThumbnailUrl { get; init; }

    public int Downloads { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public IReadOnlyList<ForgeAddonVersion> Versions { get; init; } = [];
}

/// <summary>One published version of a <see cref="ForgeAddon" />.</summary>
public sealed record ForgeAddonVersion
{
    public required int Id { get; init; }

    public required SemanticVersion Version { get; init; }

    public required VersionRange ModVersionConstraint { get; init; }

    public required string DownloadUrl { get; init; }

    public long? ContentLength { get; init; }

    public int Downloads { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>A released SPT version.</summary>
public sealed record ForgeSptVersion
{
    public required int ForgeId { get; init; }

    public required SemanticVersion Version { get; init; }

    public int ModCount { get; init; }

    public string? ReleaseUrl { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>A mod category.</summary>
public sealed record ForgeCategory
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    public required string Slug { get; init; }
}

/// <summary>One page of results, with the pagination the API reports.</summary>
public sealed record ForgePage<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public int Page { get; init; }

    public int PerPage { get; init; }

    public int Total { get; init; }

    public int LastPage { get; init; }

    public bool HasMore
    {
        get { return Page < LastPage; }
    }
}

/// <summary>A locally installed package, as the update and dependency endpoints want it.</summary>
public sealed record InstalledPackage
{
    public required string Guid { get; init; }

    public required SemanticVersion Version { get; init; }
}
