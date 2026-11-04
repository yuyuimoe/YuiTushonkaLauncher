using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// Retries the failures that are worth retrying (408, 429, 5xx, transport errors) with exponential
/// backoff plus jitter, and honours <c>Retry-After</c>. Validation errors and 404s are never retried.
/// </summary>
public sealed class ForgeRetryExecutor
{
    private readonly ForgeOptions _options;
    private readonly ILogger<ForgeRetryExecutor> _logger;
    private readonly Random _random = new();

    public ForgeRetryExecutor(IOptions<ForgeOptions> options, ILogger<ForgeRetryExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>True when a response status is worth another attempt.</summary>
    public static bool IsRetryable(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    }

    /// <summary>
    /// Runs <paramref name="send" /> until it yields a usable response, the attempts run out, or the
    /// failure is not retryable.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken
    )
    {
        var maxAttempts = Math.Max(1, _options.MaxAttempts);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;

            try
            {
                response = await send(cancellationToken);

                if (response.IsSuccessStatusCode || !IsRetryable(response.StatusCode))
                {
                    return response;
                }

                if (attempt >= maxAttempts)
                {
                    _logger.LogWarning(
                        "Forge request failed with {StatusCode} after {Attempts} attempt(s)",
                        (int)response.StatusCode,
                        attempt
                    );
                    return response;
                }

                var delay = ResolveDelay(response, attempt);
                _logger.LogInformation(
                    "Forge request returned {StatusCode}; retrying in {Delay} (attempt {Attempt}/{Max})",
                    (int)response.StatusCode,
                    delay.ToString("c", CultureInfo.InvariantCulture),
                    attempt + 1,
                    maxAttempts
                );

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt < maxAttempts)
            {
                response?.Dispose();
                var delay = Backoff(attempt);
                _logger.LogInformation(
                    exception,
                    "Forge transport failure; retrying in {Delay} (attempt {Attempt}/{Max})",
                    delay.ToString("c", CultureInfo.InvariantCulture),
                    attempt + 1,
                    maxAttempts
                );
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                response?.Dispose();
                var delay = Backoff(attempt);
                _logger.LogInformation(
                    exception,
                    "Forge request timed out; retrying in {Delay} (attempt {Attempt}/{Max})",
                    delay.ToString("c", CultureInfo.InvariantCulture),
                    attempt + 1,
                    maxAttempts
                );
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                response?.Dispose();
                throw new ForgeUnavailableException("The Forge API could not be reached.", exception);
            }
        }
    }

    private TimeSpan ResolveDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        return Backoff(attempt);
    }

    private TimeSpan Backoff(int attempt)
    {
        var baseDelay = Math.Max(1, _options.BaseRetryDelayMilliseconds);
        var exponential = baseDelay * Math.Pow(2, attempt - 1);
        var jitter = _random.Next(0, Math.Max(1, baseDelay / 2));
        return TimeSpan.FromMilliseconds(exponential + jitter);
    }
}
