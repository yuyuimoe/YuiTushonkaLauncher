using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Core.Server;
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
    public async Task TheServerListSurvivesTheRoundTrip()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        using var service = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        var settings = LauncherSettings.Default with
        {
            Servers =
            [
                ServerEntry.Local with
                {
                    Port = 7000,
                },
                new ServerEntry
                {
                    Name = "Friend",
                    Host = "10.0.0.5",
                    Port = 6969,
                },
            ],
            ActiveServer = "Friend",
        };

        await service.SaveAsync(settings);

        var loaded = await new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance).LoadAsync();

        loaded.Servers.Should().HaveCount(2);
        loaded.Servers[0].IsBuiltIn.Should().BeTrue();
        loaded.Servers[0].Port.Should().Be(7000);
        loaded.Servers[1].Address.Should().Be("10.0.0.5:6969");
        loaded.ActiveServer.Should().Be("Friend");
        loaded.Should().Be(settings, "the settings compare by value, servers included");
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

    [Fact]
    public async Task Settings_RoundTripTheWindowBehaviourAndTheStoppingQuestion()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        var settings = LauncherSettings.Default with { GameStartWindow = GameStartWindowAction.Close, AskBeforeStoppingProcesses = false };

        using (var writer = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance))
        {
            await writer.SaveAsync(settings);
        }

        using var reader = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        (await reader.LoadAsync()).Should().Be(settings);
    }

    [Fact]
    public async Task Settings_DefaultToKeepingTheWindowAndAskingBeforeStopping()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        using var service = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        var loaded = await service.LoadAsync();

        loaded.GameStartWindow.Should().Be(GameStartWindowAction.KeepOpen);
        loaded.AskBeforeStoppingProcesses.Should().BeTrue("the processes outlive the launcher, so the question is the only reminder");
    }

    [Fact]
    public async Task Settings_FallBackToKeepingTheWindow_WhenTheStoredValueIsNotAnAction()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        using (var writer = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance))
        {
            await writer.SaveAsync(LauncherSettings.Default);
        }

        await using (var context = database.CreateContext())
        {
            var row = await context.Settings.SingleAsync();
            row.GameStartWindow = "HideTheWindowSomehow";
            await context.SaveChangesAsync();
        }

        using var reader = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        (await reader.LoadAsync()).GameStartWindow.Should().Be(GameStartWindowAction.KeepOpen);
    }
}
