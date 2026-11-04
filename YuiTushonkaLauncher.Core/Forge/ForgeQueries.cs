using System;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>Query for <see cref="IForgeClient.GetModsAsync" />. Translated to <c>filter[...]</c> parameters.</summary>
public sealed record ForgeModQuery
{
    /// <summary>Full-text search (<c>query=</c>).</summary>
    public string? Search { get; init; }

    /// <summary>Exact GUIDs (<c>filter[guid]=</c>, comma separated).</summary>
    public IReadOnlyList<string> Guids { get; init; } = [];

    /// <summary>Forge ids (<c>filter[id]=</c>, comma separated).</summary>
    public IReadOnlyList<int> Ids { get; init; } = [];

    public int? HubId { get; init; }

    public int? CategoryId { get; init; }

    public string? CategorySlug { get; init; }

    /// <summary>Only mods with a version compatible with this SPT version.</summary>
    public SemanticVersion? SptVersion { get; init; }

    public bool? IsFeatured { get; init; }

    /// <summary><c>compatible</c> / <c>incompatible</c> / <c>unknown</c>.</summary>
    public string? FikaCompatibility { get; init; }

    /// <summary>Include the latest versions in each result (adds <c>include=versions</c>).</summary>
    public bool IncludeVersions { get; init; }

    /// <summary>Sort expression, e.g. <c>downloads</c> or <c>-updated_at</c>.</summary>
    public string? Sort { get; init; }

    /// <summary>Only mods published on or after this instant (<c>filter[published_between]</c>).</summary>
    public DateTimeOffset? PublishedFrom { get; init; }

    /// <summary>Only mods published on or before this instant.</summary>
    public DateTimeOffset? PublishedTo { get; init; }

    public int Page { get; init; } = 1;

    public int PerPage { get; init; } = 25;
}

/// <summary>Query for <see cref="IForgeClient.GetAddonsAsync" />.</summary>
public sealed record ForgeAddonQuery
{
    public string? Search { get; init; }

    public int? ModId { get; init; }

    public bool? IsDetached { get; init; }

    public string? Sort { get; init; }

    /// <summary>Include the versions of each addon (adds <c>include=versions</c>).</summary>
    public bool IncludeVersions { get; init; }

    /// <summary>Only addons published on or after this instant.</summary>
    public DateTimeOffset? PublishedFrom { get; init; }

    /// <summary>Only addons published on or before this instant.</summary>
    public DateTimeOffset? PublishedTo { get; init; }

    public int Page { get; init; } = 1;

    public int PerPage { get; init; } = 25;
}
