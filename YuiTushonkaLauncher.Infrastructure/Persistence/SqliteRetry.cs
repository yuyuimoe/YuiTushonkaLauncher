using Microsoft.Data.Sqlite;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>
/// Retries operations that fail because another connection holds the write lock. SQLite's
/// <c>busy_timeout</c> covers most of it; this covers the rest (a WAL checkpoint, an external
/// process such as a backup tool).
/// </summary>
internal static class SqliteRetry
{
    private const int MaxAttempts = 4;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Runs <paramref name="operation" />, retrying busy/locked failures with backoff.</summary>
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var delay = InitialDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SqliteException exception) when (IsTransient(exception) && attempt < MaxAttempts)
            {
                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
        }
    }

    /// <summary>Runs <paramref name="operation" />, retrying busy/locked failures with backoff.</summary>
    public static Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async () =>
            {
                await operation();
                return true;
            },
            cancellationToken
        );
    }

    private static bool IsTransient(SqliteException exception)
    {
        // 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED.
        return exception.SqliteErrorCode is 5 or 6;
    }
}
