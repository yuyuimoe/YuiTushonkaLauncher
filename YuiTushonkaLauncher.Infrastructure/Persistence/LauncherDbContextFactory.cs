using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using YuiTushonkaLauncher.Core.Abstractions;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>
/// Builds <see cref="LauncherDbContext" /> instances. Serves both the runtime container (through
/// <see cref="IDbContextFactory{TContext}" />) and <c>dotnet ef</c> (through
/// <see cref="IDesignTimeDbContextFactory{TContext}" />), so migrations are always generated against
/// the same options the app uses.
/// </summary>
public sealed class LauncherDbContextFactory : IDbContextFactory<LauncherDbContext>, IDesignTimeDbContextFactory<LauncherDbContext>
{
    private readonly string _databaseFile;

    /// <summary>Design-time constructor used by <c>dotnet ef</c>: writes to a throwaway file.</summary>
    public LauncherDbContextFactory()
    {
        _databaseFile = Path.Join(Path.GetTempPath(), "yuitushonka-design.db");
    }

    /// <summary>Runtime constructor.</summary>
    public LauncherDbContextFactory(IAppPaths paths)
    {
        _databaseFile = paths.DatabaseFile;
    }

    /// <summary>The SQLite file this factory opens.</summary>
    public string DatabaseFile
    {
        get { return _databaseFile; }
    }

    /// <inheritdoc />
    public LauncherDbContext CreateDbContext()
    {
        return new LauncherDbContext(BuildOptions(_databaseFile));
    }

    /// <inheritdoc />
    LauncherDbContext IDesignTimeDbContextFactory<LauncherDbContext>.CreateDbContext(string[] args)
    {
        return CreateDbContext();
    }

    /// <summary>Builds the options for a database file.</summary>
    public static DbContextOptions<LauncherDbContext> BuildOptions(string databaseFile)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 10,
        }.ToString();

        return new DbContextOptionsBuilder<LauncherDbContext>().UseSqlite(connectionString).Options;
    }
}
