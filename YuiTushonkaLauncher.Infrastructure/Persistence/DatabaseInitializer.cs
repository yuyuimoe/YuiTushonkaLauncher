using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>Prepares the database before the UI does anything: migrations, WAL and the settings row.</summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<LauncherDbContext> contextFactory,
    ISettingsService settingsService,
    IAppPaths paths,
    ILogger<DatabaseInitializer> logger
)
{
    /// <summary>Applies pending migrations and loads the settings into memory.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(paths.DatabaseFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await context.Database.MigrateAsync(cancellationToken);
            }

            // WAL is a property of the file, so setting it once is enough; it also survives upgrades.
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        logger.LogInformation("Database ready at {DatabaseFile}; language {Language}", paths.DatabaseFile, settings.Language);
    }
}
