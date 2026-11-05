using System.Text.Json;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Updates;

namespace YuiTushonkaLauncher.Infrastructure.Updates;

/// <summary>
/// Reads <c>/repos/{owner}/{repo}/releases</c> and takes the newest published release. Unauthenticated on purpose: the
/// endpoint allows 60 requests an hour per address, and the launcher asks once when the user presses the button. The
/// list rather than <c>/releases/latest</c> because "latest" is the newest <em>non-pre-release</em>: a project whose
/// only releases are pre-releases answers 404 there, and its users would never be told.
/// </summary>
public sealed class GitHubReleaseChecker(HttpClient http, ILogger<GitHubReleaseChecker> logger, string currentVersion, Uri? apiBase = null)
    : IUpdateChecker
{
    public const string DefaultRepository = "yuyuimoe/YuiTushonkaLauncher";

    /// <summary>The API root; a parameter so a test can point the check at a local server.</summary>
    public Uri ApiBase { get; } = apiBase ?? new Uri("https://api.github.com/");

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = ParseVersion(currentVersion);
        if (current is null)
        {
            return new UpdateCheckResult
            {
                Current = currentVersion,
                Reason = "This build does not carry a version, so there is nothing to compare.",
            };
        }

        try
        {
            var releasesUrl = new Uri(ApiBase, $"repos/{DefaultRepository}/releases");
            using var response = await http.GetAsync(releasesUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 404 is what a repository with no release answers; saying so is more useful than "HTTP 404".
                var reason =
                    (int)response.StatusCode == 404
                        ? "No release has been published yet."
                        : $"The release list answered {(int)response.StatusCode}.";
                return new UpdateCheckResult { Current = currentVersion, Reason = reason };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var releases = document.RootElement;
            if (releases.ValueKind != JsonValueKind.Array)
            {
                return new UpdateCheckResult { Current = currentVersion, Reason = "The release list was not a list." };
            }

            // The list is newest first. Skip drafts and anything whose tag is not a version; the first survivor is the
            // newest release, whether it is a stable release or a pre-release.
            string? firstTag = null;
            JsonElement? newestElement = null;
            SemanticVersion? newestVersion = null;
            foreach (var candidate in releases.EnumerateArray())
            {
                var candidateTag = candidate.TryGetProperty("tag_name", out var candidateTagElement)
                    ? candidateTagElement.GetString()
                    : null;
                firstTag ??= candidateTag;
                if (candidate.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (ParseVersion(candidateTag) is not { } candidateVersion)
                {
                    continue;
                }

                newestElement = candidate;
                newestVersion = candidateVersion;
                break;
            }

            if (newestElement is not { } newest)
            {
                var reason = string.IsNullOrWhiteSpace(firstTag)
                    ? "No release has been published yet."
                    : $"The release tag '{firstTag}' is not a version.";
                return new UpdateCheckResult { Current = currentVersion, Reason = reason };
            }

            var tag = newest.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var url = newest.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
            {
                return new UpdateCheckResult { Current = currentVersion, Reason = "The newest release did not name a version or a page." };
            }

            var release = new LauncherRelease
            {
                Tag = tag,
                Version = newestVersion!.Value,
                Url = url,
                Notes = newest.TryGetProperty("body", out var body) ? body.GetString() : null,
                PublishedAt =
                    newest.TryGetProperty("published_at", out var published) && published.TryGetDateTimeOffset(out var when)
                        ? when
                        : (DateTimeOffset?)null,
            };

            // A development build (X.Y.Z-pre-<sha>) is ahead of the last release by construction; telling its user to
            // "update" would mean downgrade. A released pre-release (X.Y.Z-rc.N) is a real release and must be told.
            var isDevelopmentBuild = currentVersion.Contains("-pre-", StringComparison.Ordinal);
            return new UpdateCheckResult
            {
                Current = currentVersion,
                Latest = release,
                HasUpdate = !isDevelopmentBuild && newestVersion.Value > current,
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Update check failed");
            return new UpdateCheckResult
            {
                Current = currentVersion,
                Reason = "The release list could not be read. Check the connection and try again.",
            };
        }
    }

    /// <summary>Reads <c>0.1.0</c>, <c>v0.1.0</c> and <c>0.1.0-pre-sha</c>; <c>null</c> when the text is not a version.</summary>
    public static SemanticVersion? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        var numeric = dash >= 0 ? trimmed[..dash] : trimmed;
        if (numeric.StartsWith('v'))
        {
            numeric = numeric[1..];
        }

        return SemanticVersion.TryParse(numeric, out var version) ? version : null;
    }
}
