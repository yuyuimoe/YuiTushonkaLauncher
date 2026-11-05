namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>
/// Runs a multi-step operation inside one SQLite transaction. Lives in this layer on purpose: a
/// transaction is meaningless without the context that carries it, and Core must not know EF exists.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Creates a context inside a transaction, runs <paramref name="work" />, saves and commits.
    /// An exception, or a failure while saving, rolls everything back.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<LauncherDbContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default
    );

    /// <summary>Overload for work that produces no result.</summary>
    Task ExecuteAsync(Func<LauncherDbContext, CancellationToken, Task> work, CancellationToken cancellationToken = default);
}
