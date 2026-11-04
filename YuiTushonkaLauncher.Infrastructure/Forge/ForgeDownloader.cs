using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// Downloads mod and addon archives. The API answers with a redirect to a third-party host (Dropbox,
/// GitHub, …), so redirects are followed and the final body is what counts. Interrupted downloads
/// resume through a <c>.part</c> file, and the SHA-256 is computed while streaming.
/// </summary>
public sealed class ForgeDownloader : IForgeDownloader
{
    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ForgeDownloader> _logger;

    public ForgeDownloader(HttpClient httpClient, ILogger<ForgeDownloader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ForgeDownloadResult> DownloadAsync(
        Uri url,
        string destinationDirectory,
        string fileName,
        IProgress<ForgeDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Directory.CreateDirectory(destinationDirectory);

        var finalPath = Path.Combine(destinationDirectory, fileName);
        var partPath = finalPath + ".part";
        var existingBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var response = await SendAsync(url, existingBytes, cancellationToken);
        var resumed = response.StatusCode == HttpStatusCode.PartialContent && existingBytes > 0;

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingBytes > 0)
        {
            // The partial file is already complete (or the host dislikes ranges): hash and keep it.
            _logger.LogInformation("Host rejected the resume range for {Url}; keeping the existing partial file", url);
            return await CompleteExistingAsync(partPath, finalPath, existingBytes, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ForgeUnavailableException($"Download of '{url}' failed with HTTP {(int)response.StatusCode}.");
        }

        if (!resumed)
        {
            existingBytes = 0;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        if (resumed)
        {
            await FeedExistingBytesAsync(partPath, hash, cancellationToken);
        }

        var totalBytes = response.Content.Headers.ContentLength is { } contentLength ? contentLength + existingBytes : (long?)null;
        var received = existingBytes;

        await using (
            var file = new FileStream(
                partPath,
                resumed ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true
            )
        )
        await using (var network = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                int read;
                while ((read = await network.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(new ForgeDownloadProgress(received, totalBytes));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await file.FlushAsync(cancellationToken);
        }

        var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        File.Move(partPath, finalPath, overwrite: true);

        var length = new FileInfo(finalPath).Length;
        _logger.LogInformation("Downloaded {Url} to {Path} ({Length} bytes, resumed: {Resumed})", url, finalPath, length, resumed);

        return new ForgeDownloadResult
        {
            FilePath = finalPath,
            Length = length,
            Sha256 = sha256,
            Resumed = resumed,
        };
    }

    private Task<HttpResponseMessage> SendAsync(Uri url, long existingBytes, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task FeedExistingBytesAsync(string partPath, IncrementalHash hash, CancellationToken cancellationToken)
    {
        await using var existing = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await existing.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<ForgeDownloadResult> CompleteExistingAsync(
        string partPath,
        string finalPath,
        long existingBytes,
        CancellationToken cancellationToken
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await FeedExistingBytesAsync(partPath, hash, cancellationToken);
        var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        File.Move(partPath, finalPath, overwrite: true);

        return new ForgeDownloadResult
        {
            FilePath = finalPath,
            Length = existingBytes,
            Sha256 = sha256,
            Resumed = true,
        };
    }
}
