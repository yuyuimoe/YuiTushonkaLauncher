using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

/// <summary>A migrated throw-away SQLite database plus the factory that opens it.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();

    public TestDatabase()
    {
        Paths = _temp.CreateAppPaths();
        Paths.EnsureCreated();
        Factory = new LauncherDbContextFactory(Paths);
        UnitOfWork = new EfUnitOfWork(Factory);
    }

    public AppPaths Paths { get; }

    public LauncherDbContextFactory Factory { get; }

    public IUnitOfWork UnitOfWork { get; }

    /// <summary>Applies all migrations, exactly like <c>DatabaseInitializer</c> does.</summary>
    public async Task InitializeAsync()
    {
        await using var context = Factory.CreateDbContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Opens a fresh context against the same database.</summary>
    public LauncherDbContext CreateContext()
    {
        return Factory.CreateDbContext();
    }

    public async ValueTask DisposeAsync()
    {
        // SQLite pools connections; drop them before deleting the file or Windows keeps it locked.
        SqliteConnection.ClearAllPools();
        await Task.Yield();
        _temp.Dispose();
    }
}
