using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// Downloads a mod's thumbnail once and serves it as a local file, because a list that decodes images on the UI thread
/// is a list that stutters. The cache is capped by size and count and evicts what has not been used for longest.
/// </summary>
public sealed class ThumbnailCache : IThumbnailCache, IDisposable
{
    /// <summary>No single thumbnail is allowed to be bigger than this; a mod thumbnail is a thumbnail.</summary>
    private const int MaxBytes = 512 * 1024;

    /// <summary>How many images the cache keeps before the least recently used ones go.</summary>
    private const int MaxFiles = 200;

    /// <summary>The site a thumbnail request must look like it comes from.</summary>
    private const string SiteReferrer = "https://sp-mod.com/";

    private readonly HttpClient _http;

    private readonly string _directory;

    private readonly ILogger<ThumbnailCache> _logger;

    public ThumbnailCache(IAppPaths paths, ILogger<ThumbnailCache> logger)
        : this(paths, logger, null) { }

    /// <summary>Overload for tests, which supply the client so they can count requests.</summary>
    public ThumbnailCache(IAppPaths paths, ILogger<ThumbnailCache> logger, HttpClient? client)
    {
        _directory = Path.Combine(paths.CacheDirectory, "thumbnails");
        _logger = logger;
        _http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TushonkaLauncher/1.0");
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, Key(uri) + Extension(uri));

            if (File.Exists(path))
            {
                // Touching the file is what keeps a used image out of the eviction pass.
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return path;
            }

            // The file host answers 403 without a Referer from the site (measured: 403 bare, 200 with it).
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = new Uri(SiteReferrer);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Thumbnail {Url} answered {Status}", url, (int)response.StatusCode);
                return null;
            }

            if (response.Content.Headers.ContentLength is { } length && length > MaxBytes)
            {
                _logger.LogDebug("Thumbnail {Url} is {Bytes} bytes, too big to cache", url, length);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                return null;
            }

            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            Trim();
            return path;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // A missing thumbnail is a placeholder, never an error the user has to read.
            _logger.LogDebug(exception, "Thumbnail {Url} could not be cached", url);
            return null;
        }
    }

    /// <summary>Keeps the cache inside its count, oldest use first.</summary>
    private void Trim()
    {
        try
        {
            var files = new DirectoryInfo(_directory).GetFiles().OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            foreach (var file in files.Skip(MaxFiles))
            {
                file.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "Could not trim the thumbnail cache");
        }
    }

    /// <summary>A stable file name per URL, so the same image is not downloaded twice.</summary>
    private static string Key(Uri uri)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32];
    }

    /// <summary>The extension from the URL, defaulting to <c>.img</c> for the controls that need one.</summary>
    private static string Extension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return extension.Length is > 1 and < 6 ? extension.ToLowerInvariant() : ".img";
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
