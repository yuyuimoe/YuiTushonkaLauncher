using Microsoft.EntityFrameworkCore;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>SQLite-backed <see cref="IUnitOfWork" />: one context, one transaction, all or nothing.</summary>
public sealed class EfUnitOfWork(IDbContextFactory<LauncherDbContext> contextFactory) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<LauncherDbContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await work(context, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(Func<LauncherDbContext, CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            async (context, token) =>
            {
                await work(context, token);
                return true;
            },
            cancellationToken
        );
    }
}
