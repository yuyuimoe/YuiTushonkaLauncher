namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>Configuration of the Forge API client.</summary>
public sealed class ForgeOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Forge";

    /// <summary>API root, including the version segment.</summary>
    public string BaseUrl { get; set; } = "https://sp-mod.com/api/v0/";

    /// <summary>Sent on every request so the API operator can identify the client.</summary>
    public string UserAgent { get; set; } = "YuiTushonkaLauncher (+https://sp-mod.com)";

    /// <summary>Per-request timeout.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Total attempts (first try included) for a retryable failure.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Base delay for the exponential backoff.</summary>
    public int BaseRetryDelayMilliseconds { get; set; } = 200;

    /// <summary>Requests allowed in the burst window.</summary>
    public int BurstPermits { get; set; } = 30;

    /// <summary>Burst window length.</summary>
    public int BurstWindowSeconds { get; set; } = 10;

    /// <summary>Requests allowed in the sustained window. The API advertises 300/min; stay under it.</summary>
    public int SustainedPermits { get; set; } = 240;

    /// <summary>Sustained window length.</summary>
    public int SustainedWindowSeconds { get; set; } = 60;

    /// <summary>Cache lifetime when the response carries no <c>cache-control</c> (the API sends 900).</summary>
    public int DefaultCacheSeconds { get; set; } = 900;

    /// <summary>Maximum cached responses kept in memory.</summary>
    public int CacheEntries { get; set; } = 512;

    /// <summary>Page size used when a caller does not care.</summary>
    public int DefaultPageSize { get; set; } = 25;
}
