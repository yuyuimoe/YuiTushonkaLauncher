using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_MigratesCreatesSettingsAndEnablesWal()
    {
        await using var database = new TestDatabase();
        using var settings = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);
        var initializer = new DatabaseInitializer(database.Factory, settings, database.Paths, NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync();

        await using var context = database.CreateContext();
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        var journalMode = await context.Database.SqlQueryRaw<string>("SELECT journal_mode AS Value FROM pragma_journal_mode").SingleAsync();
        journalMode.Should().Be("wal");

        settings.Current.Should().Be(LauncherSettings.Default);
    }

    [Fact]
    public async Task InitializeAsync_IsSafeToRunRepeatedly()
    {
        await using var database = new TestDatabase();
        using var settings = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);
        var initializer = new DatabaseInitializer(database.Factory, settings, database.Paths, NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        await using var context = database.CreateContext();
        (await context.Settings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_RecreatesTheDataDirectoryWhenMissing()
    {
        await using var database = new TestDatabase();
        using var settings = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);
        var initializer = new DatabaseInitializer(database.Factory, settings, database.Paths, NullLogger<DatabaseInitializer>.Instance);
        Directory.Delete(Path.GetDirectoryName(database.Paths.DatabaseFile)!, recursive: true);

        await initializer.InitializeAsync();

        File.Exists(database.Paths.DatabaseFile).Should().BeTrue();
    }
}
