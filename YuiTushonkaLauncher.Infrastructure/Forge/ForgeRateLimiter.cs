using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// Client-side rate limiting for the Forge API: a burst window plus a sustained window, both sliding.
/// The API advertises 300 requests/minute through <c>x-ratelimit-limit</c>; the defaults stay below it
/// so a burst of parallel work never trips a 429.
/// </summary>
public sealed class ForgeRateLimiter : IDisposable, IAsyncDisposable
{
    private readonly ILogger<ForgeRateLimiter> _logger;
    private readonly SlidingWindowRateLimiter _burst;
    private readonly SlidingWindowRateLimiter _sustained;
    private readonly object _gate = new();

    private int? _remaining;
    private DateTimeOffset _remainingObservedAt = DateTimeOffset.MinValue;

    public ForgeRateLimiter(IOptions<ForgeOptions> options, ILogger<ForgeRateLimiter> logger)
    {
        _logger = logger;
        var value = options.Value;

        _burst = new SlidingWindowRateLimiter(
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = value.BurstPermits,
                Window = TimeSpan.FromSeconds(value.BurstWindowSeconds),
                SegmentsPerWindow = 2,
                QueueLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }
        );

        _sustained = new SlidingWindowRateLimiter(
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = value.SustainedPermits,
                Window = TimeSpan.FromSeconds(value.SustainedWindowSeconds),
                SegmentsPerWindow = 4,
                QueueLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }
        );
    }

    /// <summary>Waits until a request may be sent.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await WaitWhileExhaustedAsync(cancellationToken);

        using var sustained = await _sustained.AcquireAsync(1, cancellationToken);
        if (sustained.IsAcquired)
        {
            using var burst = await _burst.AcquireAsync(1, cancellationToken);
            if (!burst.IsAcquired)
            {
                _logger.LogDebug("Forge burst window is saturated; request was queued");
            }

            return;
        }

        throw new Core.Forge.ForgeUnavailableException("The local Forge rate limit could not be satisfied; request cancelled.");
    }

    /// <summary>Records the quota the API reported, so an exhausted quota pauses instead of retrying blindly.</summary>
    public void ObserveRemaining(int? remaining)
    {
        if (remaining is null)
        {
            return;
        }

        lock (_gate)
        {
            _remaining = remaining;
            _remainingObservedAt = DateTimeOffset.UtcNow;
        }

        if (remaining <= 1)
        {
            _logger.LogWarning("Forge API quota almost exhausted ({Remaining} left)", remaining);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _burst.Dispose();
        _sustained.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _burst.DisposeAsync();
        await _sustained.DisposeAsync();
    }

    private async Task WaitWhileExhaustedAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay = TimeSpan.Zero;

        lock (_gate)
        {
            // The API exposes no reset timestamp; when it reports "no quota left", pause briefly and
            // let the limiter windows drain instead of firing a request that is certain to be rejected.
            if (_remaining == 0 && DateTimeOffset.UtcNow - _remainingObservedAt < TimeSpan.FromSeconds(1))
            {
                delay = TimeSpan.FromSeconds(1);
            }
        }

        if (delay > TimeSpan.Zero)
        {
            _logger.LogInformation("Forge quota exhausted; waiting {Delay}", delay.ToString("c", CultureInfo.InvariantCulture));
            await Task.Delay(delay, cancellationToken);
        }
    }
}
