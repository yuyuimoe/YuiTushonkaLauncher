using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Infrastructure.Runtime;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Runtime;

public sealed class RuntimeDefaultsAndRegistrationTests
{
    private static ProtonBuild Build(string name, string? problem = null)
    {
        return new ProtonBuild
        {
            Name = name,
            Path = "/builds/" + name,
            Root = "/builds",
            Problem = problem,
        };
    }

    [Fact]
    public void NewEnvironment_CarriesTheBepInExDllOverride()
    {
        RuntimeDefaults.NewEnvironment().Should().Contain("WINEDLLOVERRIDES", "winhttp=n,b");
        LaunchOptions.OverridesWinhttp(RuntimeDefaults.NewEnvironment()["WINEDLLOVERRIDES"]).Should().BeTrue();
    }

    [Fact]
    public void Suggest_PrefersGeProtonThenUmuThenAnythingValid()
    {
        var inventory = new RuntimeInventory
        {
            IsSupported = true,
            ProtonBuilds = [Build("Proton 9.0"), Build("UMU-Proton-9.0-3.2"), Build("GE-Proton11-1"), Build("GE-Proton10-27")],
        };

        var draft = RuntimeDefaults.Suggest(inventory, "mine")!;

        draft.Name.Should().Be("mine");
        draft.Kind.Should().Be(RuntimeKind.Proton);
        draft.Executable.Should().Be("/builds/GE-Proton11-1", "the first GE build in the already newest-first list");
        draft.MakeDefault.Should().BeTrue();
        draft.Environment.Should().ContainKey("WINEDLLOVERRIDES");

        var withoutGe = inventory with { ProtonBuilds = [Build("Proton 9.0"), Build("UMU-Proton-9.0-3.2")] };
        RuntimeDefaults.Suggest(withoutGe)!.Executable.Should().Be("/builds/UMU-Proton-9.0-3.2");

        var onlyOther = inventory with { ProtonBuilds = [Build("Proton 9.0")] };
        RuntimeDefaults.Suggest(onlyOther)!.Executable.Should().Be("/builds/Proton 9.0");
    }

    [Fact]
    public void Suggest_IgnoresInvalidBuildsAndFallsBackToWineThenToNothing()
    {
        var broken = new RuntimeInventory { IsSupported = true, ProtonBuilds = [Build("GE-Proton11-1", problem: "broken")] };

        RuntimeDefaults.Suggest(broken).Should().BeNull();

        var wine = RuntimeDefaults.Suggest(broken with { Wine = "/usr/bin/wine" })!;
        wine.Kind.Should().Be(RuntimeKind.Wine);
        wine.Executable.Should().Be("/usr/bin/wine");
    }

    [Fact]
    public void Suggest_OnAnUnsupportedPlatformIsNothing()
    {
        RuntimeDefaults.Suggest(RuntimeInventory.Unsupported).Should().BeNull();
    }

    [Fact]
    public void AddLauncherRuntime_RegistersEveryServiceOnce()
    {
        var provider = new ServiceCollection().AddLogging().AddLauncherPersistence().AddLauncherRuntime();
        using var scope = provider.AddSingleton<IAppPaths>(new TempDirectory().CreateAppPaths()).BuildServiceProvider();

        scope.GetRequiredService<IPlatformInfo>().Should().BeOfType<PlatformInfo>();
        scope.GetRequiredService<IRuntimeLocator>().Should().BeOfType<RuntimeLocator>();
        scope.GetRequiredService<IRuntimeLauncher>().Should().BeOfType<RuntimeLauncher>();
        scope.GetRequiredService<IProcessRunner>().Should().BeOfType<ProcessRunner>();
        scope.GetRequiredService<IPrefixService>().Should().BeOfType<PrefixService>();
        scope.GetRequiredService<IRuntimeProfileService>().Should().BeOfType<RuntimeProfileService>();
        scope.GetRequiredService<IRuntimeLauncher>().Should().BeSameAs(scope.GetRequiredService<IRuntimeLauncher>());
    }

    [Fact]
    public async Task Settings_RoundTripTheExtraProtonRootsAndTheUmuPath()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        var settings = LauncherSettings.Default with
        {
            ExtraProtonRoots = ["/opt/protons", "/mnt/games/my protons"],
            UmuRunPath = "/opt/umu/umu-run",
        };

        using (var writer = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance))
        {
            await writer.SaveAsync(settings);
        }

        using var reader = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);
        var loaded = await reader.LoadAsync();

        loaded.ExtraProtonRoots.Should().Equal("/opt/protons", "/mnt/games/my protons");
        loaded.UmuRunPath.Should().Be("/opt/umu/umu-run");
    }

    [Fact]
    public async Task Settings_ATamperedRootsColumnFallsBackToNothingInsteadOfBlockingStartup()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();

        using (var seed = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance))
        {
            await seed.LoadAsync();
        }

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE Settings SET ExtraProtonRoots = 'not json at all'");
        }

        using var reader = new EfSettingsService(database.Factory, NullLogger<EfSettingsService>.Instance);

        (await reader.LoadAsync()).ExtraProtonRoots.Should().BeEmpty();
    }

    [Fact]
    public void LauncherSettings_AreEqualWhenTheirRootListsHaveTheSameFoldersAndDifferOtherwise()
    {
        var a = LauncherSettings.Default with { ExtraProtonRoots = ["/a", "/b"], UmuRunPath = "/umu" };
        var same = LauncherSettings.Default with { ExtraProtonRoots = ["/a", "/b"], UmuRunPath = "/umu" };

        a.Should().Be(same);
        a.GetHashCode().Should().Be(same.GetHashCode());
        (a with { ExtraProtonRoots = ["/a"] }).Should().NotBe(a);
        (a with { ExtraProtonRoots = ["/b", "/a"] }).Should().NotBe(a, "order is the search order");
        (a with { UmuRunPath = null }).Should().NotBe(a);
        (a with { Language = "pt-BR" }).Should().NotBe(a);
        a.Equals((LauncherSettings?)null).Should().BeFalse();
    }
}
