using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>How the catalogue is ordered.</summary>
public enum ForgeSort
{
    /// <summary>The Forge's own featured flag.</summary>
    Featured,

    /// <summary>Most endorsed among the mods published in the last seven days.</summary>
    EndorsedWeek,

    /// <summary>Most endorsed among the mods published in the last thirty days.</summary>
    EndorsedMonth,

    /// <summary>Most endorsed overall.</summary>
    EndorsedAllTime,

    /// <summary>Newest first.</summary>
    Newest,

    /// <summary>Most recently updated first.</summary>
    RecentlyUpdated,

    /// <summary>Alphabetical.</summary>
    Name,
}

/// <summary>One entry of the sort picker.</summary>
/// <param name="Sort">The ordering it selects.</param>
/// <param name="LabelKey">Localisation key of its label.</param>
public sealed record ForgeSortChoice(ForgeSort Sort, string LabelKey);

/// <summary>
/// Turns a <see cref="ForgeSort" /> into the parameters the API understands. The API sorts by
/// <c>name, downloads, favourites_count, endorsements_count, featured, created_at, updated_at, published_at</c>, and it
/// has no per-period endorsement count: a windowed ranking is therefore <em>mods published inside the window, ordered
/// by their total endorsements</em>, and the UI says so instead of pretending otherwise.
/// </summary>
public static class ForgeSorts
{
    /// <summary>Every ordering, in the order the picker shows them.</summary>
    public static IReadOnlyList<ForgeSortChoice> All { get; } =
    [
        new(ForgeSort.Featured, "forge.sortFeatured"),
        new(ForgeSort.EndorsedWeek, "forge.sortEndorsedWeek"),
        new(ForgeSort.EndorsedMonth, "forge.sortEndorsedMonth"),
        new(ForgeSort.EndorsedAllTime, "forge.sortEndorsed"),
        new(ForgeSort.Newest, "forge.sortNewest"),
        new(ForgeSort.RecentlyUpdated, "forge.sortUpdated"),
        new(ForgeSort.Name, "forge.sortName"),
    ];

    /// <summary>Applies an ordering to a query, keeping whatever filters it already carried.</summary>
    public static ForgeModQuery Apply(ForgeModQuery query, ForgeSort sort, DateTimeOffset now)
    {
        return sort switch
        {
            ForgeSort.Featured => query with { Sort = "featured", PublishedFrom = null, PublishedTo = null },
            ForgeSort.EndorsedWeek => query with { Sort = "-endorsements_count", PublishedFrom = now.AddDays(-7), PublishedTo = now },
            ForgeSort.EndorsedMonth => query with { Sort = "-endorsements_count", PublishedFrom = now.AddDays(-30), PublishedTo = now },
            ForgeSort.EndorsedAllTime => query with { Sort = "-endorsements_count", PublishedFrom = null, PublishedTo = null },
            ForgeSort.Newest => query with { Sort = "-published_at", PublishedFrom = null, PublishedTo = null },
            ForgeSort.RecentlyUpdated => query with { Sort = "-updated_at", PublishedFrom = null, PublishedTo = null },
            ForgeSort.Name => query with { Sort = "name", PublishedFrom = null, PublishedTo = null },
            _ => query,
        };
    }
}

/// <summary>Whether a published version can be used with an SPT release.</summary>
public static class ForgeVersionCompatibility
{
    /// <summary>
    /// True when the version supports <paramref name="sptVersion" />. The API sometimes resolves the concrete versions a
    /// constraint allows; when it did, that list is authoritative, otherwise the constraint itself is evaluated. A
    /// missing SPT version means "no instance is open", which cannot reject anything.
    /// </summary>
    public static bool IsCompatible(ForgeModVersion version, SemanticVersion? sptVersion)
    {
        if (sptVersion is not { } spt)
        {
            return true;
        }

        if (version.SupportedSptVersions.Count > 0)
        {
            return version.SupportedSptVersions.Contains(spt);
        }

        return version.SptConstraint.Satisfies(spt);
    }

    /// <summary>The versions usable with this SPT release, newest first.</summary>
    public static IReadOnlyList<ForgeModVersion> Compatible(IEnumerable<ForgeModVersion> versions, SemanticVersion? sptVersion)
    {
        return [.. versions.Where(version => IsCompatible(version, sptVersion)).OrderByDescending(version => version.Version)];
    }
}

/// <summary>What the mod's own page says that the API does not: licence, source code and VirusTotal links.</summary>
public sealed record ForgeModPageInfo
{
    /// <summary>Licence name as the page shows it (<c>MIT License</c>).</summary>
    public string? License { get; init; }

    /// <summary>Where the licence text lives, when the page links one.</summary>
    public string? LicenseUrl { get; init; }

    /// <summary>Source-code or repository links the page lists.</summary>
    public IReadOnlyList<string> SourceCodeUrls { get; init; } = [];

    /// <summary>VirusTotal report links (one per scan hash).</summary>
    public IReadOnlyList<string> VirusTotalUrls { get; init; } = [];

    /// <summary>Optional scan summary as the page renders it (<c>0/72 security vendors flagged this file</c>).</summary>
    public string? ScanSummary { get; init; }

    /// <summary>Nothing was found: the section is hidden rather than shown empty.</summary>
    public bool IsEmpty
    {
        get { return License is null && SourceCodeUrls.Count == 0 && VirusTotalUrls.Count == 0; }
    }
}

/// <summary>
/// Reads the parts of a mod page the API does not expose. It is a best-effort reader of the site's HTML: a page that
/// cannot be read, or that changed shape, produces <c>null</c>, and the screen simply hides those links.
/// </summary>
public interface IForgePageReader
{
    /// <summary>Reads licence, source-code and VirusTotal links for a mod, or <c>null</c> when the page cannot be read.</summary>
    Task<ForgeModPageInfo?> ReadAsync(int modId, string slug, CancellationToken cancellationToken = default);

    /// <summary>Parses a page that is already in memory; exposed so a fixture can be tested without the network.</summary>
    ForgeModPageInfo? Parse(string html);
}

/// <summary>
/// Hands out a local file for a mod thumbnail, downloading it once. The browser asks for a path, never for bytes:
/// image decoding on the UI thread is what makes a long list stutter.
/// </summary>
public interface IThumbnailCache
{
    /// <summary>Local path of the thumbnail, or <c>null</c> when there is none or it could not be fetched.</summary>
    Task<string?> GetAsync(string? url, CancellationToken cancellationToken = default);
}
