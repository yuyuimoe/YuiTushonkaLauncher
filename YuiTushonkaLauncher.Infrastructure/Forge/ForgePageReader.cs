using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// Reads licence, source-code and VirusTotal links off a mod's page, because the API does not expose them: verified
/// against the live API, <c>/mod/{id}</c> carries the description and nothing else, while the page renders labelled
/// sections that hold exactly those three things.
/// </summary>
/// <remarks>
/// This is a forgiving reader of the site's HTML. Every pattern is optional, a page that changed shape produces fewer
/// links rather than an exception, and a page that cannot be fetched produces nothing at all.
/// </remarks>
public sealed partial class ForgePageReader : IForgePageReader
{
    /// <summary>The site the pages live on; the API lives under <c>/api/v0</c> of the same host.</summary>
    public const string DefaultBaseUrl = "https://sp-mod.com";

    private readonly HttpClient _client;

    private readonly string _baseUrl;

    private readonly ILogger<ForgePageReader> _logger;

    public ForgePageReader(HttpClient client, ILogger<ForgePageReader> logger, string? baseUrl = null)
    {
        _client = client;
        _logger = logger;
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<ForgeModPageInfo?> ReadAsync(int modId, string slug, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/mod/{modId}/{(string.IsNullOrWhiteSpace(slug) ? "mod" : slug)}";

        try
        {
            using var response = await _client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Mod page {Url} answered {Status}", url, (int)response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var info = Parse(html);
            _logger.LogDebug(
                "Mod page {Url}: licence {License}, {Sources} source links, {Scans} scans",
                url,
                info?.License ?? "none",
                info?.SourceCodeUrls.Count ?? 0,
                info?.VirusTotalUrls.Count ?? 0
            );

            return info;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _logger.LogDebug(exception, "Mod page {Url} could not be read", url);
            return null;
        }
    }

    /// <inheritdoc />
    public ForgeModPageInfo? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var license = Anchors(Section(html, "License")).FirstOrDefault();
        var sources = Anchors(Section(html, "Source Code"))
            .Select(anchor => anchor.Href)
            .Where(href => IsWeb(href) && !IsVirusTotal(href) && !href.Contains("choosealicense", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var scans = Anchors(Section(html, "VirusTotal"))
            .Select(anchor => anchor.Href)
            .Where(IsVirusTotal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ForgeModPageInfo
        {
            License = license is null ? null : license.Title ?? license.Text,
            LicenseUrl = license is { } found && IsWeb(found.Href) ? found.Href : null,
            SourceCodeUrls = sources,
            VirusTotalUrls = scans,
        };
    }

    /// <summary>The HTML of one labelled section, up to the end of its list item.</summary>
    private static string? Section(string html, string label)
    {
        foreach (Match match in LabeledSection().Matches(html))
        {
            var found = match.Groups["label"].Value.Trim();
            if (string.Equals(found, label, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["body"].Value;
            }
        }

        // The scans section is titled "Latest Version VirusTotal Results" on the page.
        if (string.Equals(label, "VirusTotal", StringComparison.OrdinalIgnoreCase))
        {
            return Section(html, "Latest Version VirusTotal Results");
        }

        return null;
    }

    /// <summary>Every anchor in a section, with its URL, its title attribute and its text.</summary>
    private static List<Anchor> Anchors(string? section)
    {
        if (section is null)
        {
            return [];
        }

        var anchors = new List<Anchor>();
        foreach (Match element in AnchorHtml().Matches(section))
        {
            var href = HrefMatch().Match(element.Value);
            if (!href.Success)
            {
                continue;
            }

            var title = TitleMatch().Match(element.Value);
            var text = TextMatch().Match(element.Value);
            anchors.Add(
                new Anchor(
                    href.Groups[1].Value,
                    title.Success ? WebUtility.HtmlDecode(title.Groups[1].Value).Trim() : null,
                    text.Success ? WebUtility.HtmlDecode(text.Groups[1].Value).Trim() : null
                )
            );
        }

        return anchors;
    }

    private static bool IsWeb(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    }

    private static bool IsVirusTotal(string url)
    {
        return url.Contains("virustotal.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An anchor the page renders: where it points, what it says it is, and what it reads as.</summary>
    private sealed record Anchor(string Href, string? Title, string? Text);

    [GeneratedRegex(@"<h3[^>]*>\s*(?<label>[^<]{1,60}?)\s*</h3>(?<body>.{0,4000}?)</li>", RegexOptions.Singleline)]
    private static partial Regex LabeledSection();

    [GeneratedRegex(@"<a\s[^>]*>.*?</a>", RegexOptions.Singleline)]
    private static partial Regex AnchorHtml();

    [GeneratedRegex(@"href=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefMatch();

    [GeneratedRegex(@"title=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex TitleMatch();

    [GeneratedRegex(@">\s*([^<]+?)\s*</a>", RegexOptions.Singleline)]
    private static partial Regex TextMatch();
}
