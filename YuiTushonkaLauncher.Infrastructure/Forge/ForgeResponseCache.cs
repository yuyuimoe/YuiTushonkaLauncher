using Microsoft.Extensions.Options;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// In-memory cache for GET responses, honouring the <c>cache-control: max-age</c> the API sends
/// (currently 900 s). Keeps repeated navigation off the network without hiding fresh data.
/// </summary>
public sealed class ForgeResponseCache
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly TimeSpan _defaultTtl;

    public ForgeResponseCache(IOptions<ForgeOptions> options)
    {
        _maxEntries = Math.Max(1, options.Value.CacheEntries);
        _defaultTtl = TimeSpan.FromSeconds(Math.Max(1, options.Value.DefaultCacheSeconds));
    }

    /// <summary>Number of entries currently stored (expired ones included until the next sweep).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Computes the lifetime of a response from its headers.</summary>
    public TimeSpan ResolveTtl(HttpResponseMessage response)
    {
        var maxAge = response.Headers.CacheControl?.MaxAge;
        if (maxAge is { } value && value > TimeSpan.Zero)
        {
            return value;
        }

        return _defaultTtl;
    }

    /// <summary>Returns a cached body when it is still fresh.</summary>
    public bool TryGet(string key, out string body)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    body = entry.Body;
                    return true;
                }

                _entries.Remove(key);
            }
        }

        body = string.Empty;
        return false;
    }

    /// <summary>Stores a body until <paramref name="ttl" /> elapses.</summary>
    public void Set(string key, string body, TimeSpan ttl)
    {
        lock (_gate)
        {
            if (_entries.Count >= _maxEntries)
            {
                Sweep();
            }

            _entries[key] = new Entry(body, DateTimeOffset.UtcNow + ttl);
        }
    }

    /// <summary>Drops everything, e.g. when the user asks for a refresh.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToList();
        foreach (var key in expired)
        {
            _entries.Remove(key);
        }

        // Still full: drop the entry closest to expiry so the cache stays bounded.
        if (_entries.Count >= _maxEntries)
        {
            var oldest = _entries.OrderBy(pair => pair.Value.ExpiresAt).First().Key;
            _entries.Remove(oldest);
        }
    }

    private readonly record struct Entry(string Body, DateTimeOffset ExpiresAt);
}
