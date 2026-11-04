using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class EfSettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_CreatesTheDefaultRowOnFirstRun()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        using var service = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        var settings = await service.LoadAsync();

        settings.Should().Be(LauncherSettings.Default);

        await using var context = database.CreateContext();
        (await context.Settings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsEveryValue()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        var settings = LauncherSettings.Default with
        {
            Language = "pt-BR",
            Theme = "Dark",
            MaxConcurrentDownloads = 6,
            LogRetentionDays = 30,
            LogRetentionFiles = 12,
            FirstRunCompleted = true,
            InstancesRoot = "/games/spt/instances",
        };

        using (var writer = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance))
        {
            await writer.SaveAsync(settings);
        }

        using var reader = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);
        var loaded = await reader.LoadAsync();

        loaded.Should().Be(settings);
    }

    [Fact]
    public async Task SaveAsync_UpdatesTheExistingRowInsteadOfInserting()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        using var service = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        await service.SaveAsync(LauncherSettings.Default with { Language = "pt-BR" });
        await service.SaveAsync(LauncherSettings.Default with { Language = "en" });

        await using var context = database.CreateContext();
        (await context.Settings.CountAsync()).Should().Be(1);
        (await context.Settings.SingleAsync()).Language.Should().Be("en");
    }

    [Fact]
    public async Task SaveAsync_RaisesChangedWithTheNewValue()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        using var service = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        LauncherSettings? observed = null;
        service.Changed += (_, updated) => observed = updated;
        var settings = LauncherSettings.Default with { Theme = "Light" };

        await service.SaveAsync(settings);

        observed.Should().Be(settings);
        service.Current.Should().Be(settings);
    }
}
