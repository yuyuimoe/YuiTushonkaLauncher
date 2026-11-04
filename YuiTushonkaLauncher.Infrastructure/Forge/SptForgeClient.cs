using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge.Dtos;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>Client for <c>https://sp-mod.com/api/v0</c>.</summary>
/// <remarks>
/// The API needs no authentication; it only asks for a descriptive <c>User-Agent</c>. Everything else
/// (rate limiting, caching, retries) is handled here so callers just ask for data.
/// </remarks>
public sealed class SptForgeClient : IForgeClient
{
    private const int MaxPagesPerRequest = 20;
    private const int GuidChunkSize = 40;
    private const int InstalledChunkSize = 40;

    private readonly HttpClient _httpClient;
    private readonly ForgeOptions _options;
    private readonly ForgeRateLimiter _rateLimiter;
    private readonly ForgeRetryExecutor _retry;
    private readonly ForgeResponseCache _cache;
    private readonly ILogger<SptForgeClient> _logger;

    public SptForgeClient(
        HttpClient httpClient,
        IOptions<ForgeOptions> options,
        ForgeRateLimiter rateLimiter,
        ForgeRetryExecutor retry,
        ForgeResponseCache cache,
        ILogger<SptForgeClient> logger
    )
    {
        _httpClient = httpClient;
        _options = options.Value;
        _rateLimiter = rateLimiter;
        _retry = retry;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ForgePing> PingAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync<PingDto>("ping", cancellationToken);
        return new ForgePing { Message = envelope.Data?.Message ?? string.Empty };
    }

    /// <inheritdoc />
    public async Task<ForgePage<ForgeMod>> GetModsAsync(ForgeModQuery query, CancellationToken cancellationToken = default)
    {
        var url = new ForgeQueryString()
            .Add("query", query.Search)
            .AddFilter("guid", query.Guids)
            .AddFilter("id", query.Ids.Select(id => id.ToString(CultureInfo.InvariantCulture)))
            .AddFilter("hub_id", query.HubId)
            .AddFilter("category_id", query.CategoryId)
            .AddFilter("category_slug", query.CategorySlug)
            .AddFilter("spt_version", query.SptVersion?.ToString())
            .AddFilter("featured", query.IsFeatured)
            .AddFilter("fika_compatibility", query.FikaCompatibility)
            .Add("include", query.IncludeVersions ? "versions" : null)
            .Add("sort", query.Sort)
            .AddFilter("published_between", Window(query.PublishedFrom, query.PublishedTo))
            .Add("page", query.Page)
            .Add("per_page", query.PerPage <= 0 ? _options.DefaultPageSize : query.PerPage);

        var envelope = await GetEnvelopeAsync<List<ModDto>>($"mods{url}", cancellationToken);

        return new ForgePage<ForgeMod>
        {
            Items = envelope.Data is null ? [] : [.. envelope.Data.Select(ForgeMapper.ToModel)],
            Page = envelope.Meta?.CurrentPage ?? query.Page,
            PerPage = envelope.Meta?.PerPage ?? query.PerPage,
            Total = envelope.Meta?.Total ?? 0,
            LastPage = envelope.Meta?.LastPage ?? 1,
        };
    }

    /// <summary>
    /// The between filter, in the only shape the API reads: <c>YYYY-MM-DD,YYYY-MM-DD</c>. Anything else is silently
    /// ignored, so a malformed window is not sent at all.
    /// </summary>
    private static string? Window(DateTimeOffset? from, DateTimeOffset? to)
    {
        return from is { } start && to is { } end
            ? string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd},{1:yyyy-MM-dd}", start, end)
            : null;
    }

    /// <inheritdoc />
    public async Task<ForgeMod> GetModAsync(int modId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync<ModDto>(FormattableString.Invariant($"mod/{modId}"), cancellationToken);

        if (envelope.Data is null)
        {
            throw new ForgeException($"The API returned no data for mod {modId}.");
        }

        return ForgeMapper.ToModel(envelope.Data);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeModVersion>> GetModVersionsAsync(int modId, CancellationToken cancellationToken = default)
    {
        var versions = new List<ForgeModVersion>();

        for (var page = 1; page <= MaxPagesPerRequest; page++)
        {
            var url = new ForgeQueryString().Add("page", page).Add("per_page", 100);
            var envelope = await GetEnvelopeAsync<List<ModVersionDto>>(
                FormattableString.Invariant($"mod/{modId}/versions{url}"),
                cancellationToken
            );

            if (envelope.Data is null || envelope.Data.Count == 0)
            {
                break;
            }

            versions.AddRange(envelope.Data.Select(ForgeMapper.ToModel));

            var lastPage = envelope.Meta?.LastPage ?? 1;
            if (page >= lastPage)
            {
                break;
            }
        }

        return versions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeMod>> FindModsByGuidsAsync(
        IReadOnlyCollection<string> guids,
        CancellationToken cancellationToken = default
    )
    {
        var wanted = guids.Where(guid => !string.IsNullOrWhiteSpace(guid)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var matches = new Dictionary<string, ForgeMod>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in wanted.Chunk(GuidChunkSize))
        {
            var url = new ForgeQueryString().AddFilter("guid", chunk).Add("include", "versions").Add("per_page", 100);
            var envelope = await GetEnvelopeAsync<List<ModDto>>($"mods{url}", cancellationToken);

            foreach (var dto in envelope.Data ?? [])
            {
                // The filter is exact, but never trust a server-side match to be case- and
                // whitespace-perfect: the GUID is the identity, so verify it locally too.
                if (dto.Guid is not null && wanted.Contains(dto.Guid.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    matches[dto.Guid.Trim()] = ForgeMapper.ToModel(dto);
                }
            }
        }

        return [.. matches.Values];
    }

    /// <inheritdoc />
    public async Task<ForgeUpdateReport> CheckUpdatesAsync(
        IReadOnlyCollection<InstalledPackage> installed,
        SemanticVersion sptVersion,
        CancellationToken cancellationToken = default
    )
    {
        if (installed.Count == 0)
        {
            return new ForgeUpdateReport { SptVersion = sptVersion };
        }

        var updates = new List<ForgeUpdate>();
        var blocked = new List<ForgeUpdateTarget>();
        var upToDate = new List<ForgeUpdateTarget>();
        var incompatible = new List<ForgeIncompatible>();

        foreach (var chunk in installed.Chunk(InstalledChunkSize))
        {
            // Format is "identifier:version,..." where the identifier may be a numeric mod id or a GUID.
            var mods = string.Join(',', chunk.Select(package => $"{package.Guid}:{package.Version}"));
            var url = new ForgeQueryString().Add("mods", mods).Add("spt_version", sptVersion.ToString());
            var envelope = await GetEnvelopeAsync<UpdatesDto>($"mods/updates{url}", cancellationToken);

            if (envelope.Data is null)
            {
                continue;
            }

            var report = ForgeMapper.ToModel(envelope.Data, sptVersion);
            updates.AddRange(report.Updates);
            blocked.AddRange(report.BlockedUpdates);
            upToDate.AddRange(report.UpToDate);
            incompatible.AddRange(report.Incompatible);
        }

        _logger.LogDebug(
            "Update check for SPT {SptVersion}: {Updates} update(s), {Incompatible} incompatible",
            sptVersion,
            updates.Count,
            incompatible.Count
        );

        return new ForgeUpdateReport
        {
            SptVersion = sptVersion,
            Updates = updates,
            BlockedUpdates = blocked,
            UpToDate = upToDate,
            Incompatible = incompatible,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>> GetDependenciesAsync(
        IReadOnlyCollection<InstalledPackage> packages,
        SemanticVersion sptVersion,
        CancellationToken cancellationToken = default
    )
    {
        var result = new Dictionary<string, IReadOnlyList<ForgeDependency>>(StringComparer.OrdinalIgnoreCase);
        if (packages.Count == 0)
        {
            return result;
        }

        foreach (var chunk in packages.Chunk(InstalledChunkSize))
        {
            var mods = string.Join(',', chunk.Select(package => $"{package.Guid}:{package.Version}"));
            var url = new ForgeQueryString().Add("mods", mods).Add("spt_version", sptVersion.ToString());
            var envelope = await GetEnvelopeAsync<Dictionary<string, List<DependencyDto>>>(($"mods/dependencies{url}"), cancellationToken);

            foreach (var pair in envelope.Data ?? [])
            {
                result[pair.Key] = [.. (pair.Value ?? []).Select(ForgeMapper.ToModel)];
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ForgePage<ForgeAddon>> GetAddonsAsync(ForgeAddonQuery query, CancellationToken cancellationToken = default)
    {
        var url = new ForgeQueryString()
            .Add("query", query.Search)
            .Add("sort", query.Sort)
            .AddFilter("published_between", Window(query.PublishedFrom, query.PublishedTo))
            .Add("page", query.Page)
            .Add("per_page", query.PerPage <= 0 ? _options.DefaultPageSize : query.PerPage);

        var envelope = await GetEnvelopeAsync<List<AddonDto>>($"addons{url}", cancellationToken);
        var items = envelope.Data is null ? [] : envelope.Data.Select(ForgeMapper.ToModel).ToList();

        // Addon filters are not documented; when a parent mod is requested, filter locally.
        if (query.ModId is { } modId)
        {
            items = [.. items.Where(addon => addon.ModId == modId)];
        }

        if (query.IsDetached is { } isDetached)
        {
            items = [.. items.Where(addon => addon.IsDetached == isDetached)];
        }

        return new ForgePage<ForgeAddon>
        {
            Items = items,
            Page = envelope.Meta?.CurrentPage ?? query.Page,
            PerPage = envelope.Meta?.PerPage ?? query.PerPage,
            Total = envelope.Meta?.Total ?? items.Count,
            LastPage = envelope.Meta?.LastPage ?? 1,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeAddonVersion>> GetAddonVersionsAsync(int addonId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync<List<AddonVersionDto>>(
            FormattableString.Invariant($"addon/{addonId}/versions"),
            cancellationToken
        );
        return envelope.Data is null ? [] : [.. envelope.Data.Select(ForgeMapper.ToModel)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeSptVersion>> GetSptVersionsAsync(int perPage = 50, CancellationToken cancellationToken = default)
    {
        var url = new ForgeQueryString().Add("per_page", perPage);
        var envelope = await GetEnvelopeAsync<List<SptVersionDto>>($"spt/versions{url}", cancellationToken);
        return envelope.Data is null ? [] : [.. envelope.Data.Select(ForgeMapper.ToModel)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var url = new ForgeQueryString().Add("per_page", 100);
        var envelope = await GetEnvelopeAsync<List<CategoryDto>>($"mod-categories{url}", cancellationToken);
        return envelope.Data is null ? [] : [.. envelope.Data.Select(ForgeMapper.ToModel)];
    }

    private async Task<ApiEnvelope<T>> GetEnvelopeAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var json = await GetJsonAsync(relativeUrl, cancellationToken);

        ApiEnvelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(json, ForgeMapper.JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ForgeException($"The Forge API returned malformed JSON for '{relativeUrl}'.", exception);
        }

        if (envelope is null)
        {
            throw new ForgeException($"The Forge API returned an empty body for '{relativeUrl}'.");
        }

        if (!envelope.Success)
        {
            throw new ForgeRequestException(200, envelope.Code, envelope.Message ?? "The API reported a failure.");
        }

        return envelope;
    }

    private async Task<string> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(relativeUrl, out var cached))
        {
            _logger.LogTrace("Forge cache hit for {Url}", relativeUrl);
            return cached;
        }

        using var response = await _retry.SendAsync(
            async token =>
            {
                await _rateLimiter.WaitAsync(token);
                using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
                return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            },
            cancellationToken
        );

        _rateLimiter.ObserveRemaining(ReadRemaining(response));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryReadError(body);
            throw new ForgeRequestException((int)response.StatusCode, code, message ?? response.ReasonPhrase ?? "request failed");
        }

        _cache.Set(relativeUrl, body, _cache.ResolveTtl(response));
        return body;
    }

    private static (string? Code, string? Message) TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(body, ForgeMapper.JsonOptions);
            return (envelope?.Code, envelope?.Message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static int? ReadRemaining(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var values))
        {
            var first = values.FirstOrDefault();
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
            {
                return remaining;
            }
        }

        return null;
    }
}
