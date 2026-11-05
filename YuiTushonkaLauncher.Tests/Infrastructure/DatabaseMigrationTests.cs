using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task Migrate_CreatesTheExpectedSchema()
    {
        await using var database = new TestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();
        var tables = await context
            .Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();

        tables
            .Should()
            .Contain([
                "Instances",
                "InstalledMods",
                "InstalledFiles",
                "SptReleases",
                "Mods",
                "ModVersions",
                "Addons",
                "AddonVersions",
                "Dependencies",
                "ModLists",
                "ModListEntries",
                "RuntimeProfiles",
                "ServerProfiles",
                "Settings",
            ]);
    }

    [Fact]
    public async Task Migrate_IsIdempotent()
    {
        await using var database = new TestDatabase();

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var context = database.CreateContext();
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DatabaseFile_IsCreatedUnderTheConfiguredDataRoot()
    {
        await using var database = new TestDatabase();

        await database.InitializeAsync();

        File.Exists(database.Paths.DatabaseFile).Should().BeTrue();
    }

    [Fact]
    public async Task ForeignKeys_AreEnforced()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();

        await using var context = database.CreateContext();
        context.InstalledMods.Add(
            new InstalledMod
            {
                Id = Guid.NewGuid(),
                InstanceId = Guid.NewGuid(),
                Guid = "com.example.orphan",
                Name = "Orphan",
                Version = SemanticVersion.Parse("1.0.0"),
            }
        );

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("an install cannot exist without its instance");
    }
}
