using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>Why a server that was started did not become ready.</summary>
internal sealed class ServerNotReadyException(bool processExited, string message) : Exception(message)
{
    public bool ProcessExited { get; } = processExited;
}

/// <summary>
/// Waits for <c>ping</c> by polling, never by sleeping a fixed time: a slow start still works, and a crash is
/// reported at once. A ping only counts while the process we started is still alive, because a different
/// server on the same port would answer it just as well.
/// </summary>
internal static class ServerReadiness
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(2);

    public static async Task<TimeSpan> WaitAsync(
        ISptServerClient client,
        TimeSpan timeout,
        Func<bool> processAlive,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var started = time.GetTimestamp();
        var delay = FirstDelay;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!processAlive())
            {
                throw new ServerNotReadyException(true, "The server process ended before it answered ping.");
            }

            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attempt.CancelAfter(AttemptTimeout);

                try
                {
                    if (await client.PingAsync(attempt.Token) && processAlive())
                    {
                        return time.GetElapsedTime(started);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // This attempt timed out; the next one may succeed.
                }
            }

            var elapsed = time.GetElapsedTime(started);

            if (elapsed >= timeout)
            {
                throw new ServerNotReadyException(false, $"The server did not answer ping within {timeout.TotalSeconds:0} seconds.");
            }

            await Task.Delay(delay, time, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
        }
    }
}
