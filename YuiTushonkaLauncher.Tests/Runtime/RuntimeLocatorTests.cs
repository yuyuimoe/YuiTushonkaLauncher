using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Tests.Runtime;

public sealed class RuntimeLocatorTests
{
    [Fact]
    public void Locate_ListsValidBuildsNewestFirstWithNaturalOrdering()
    {
        using var world = new RuntimeWorld();
        world.CreateProton("GE-Proton10-9");
        world.CreateProton("GE-Proton10-27");
        world.CreateProton("GE-Proton9-20");

        var inventory = world.CreateLocator().Locate();

        inventory.IsSupported.Should().BeTrue();
        inventory.ProtonBuilds.Select(build => build.Name).Should().Equal("GE-Proton10-27", "GE-Proton10-9", "GE-Proton9-20");
        inventory.ProtonBuilds.Should().OnlyContain(build => build.IsValid);
    }

    [Fact]
    public void Locate_ReportsABrokenBuildWithItsExactPathAndReason()
    {
        using var world = new RuntimeWorld();
        var noScript = world.CreateProton("NoScript", script: false);
        var noManifest = world.CreateProton("NoManifest", manifest: false);
        var noFiles = world.CreateProton("NoFiles", files: false);

        var builds = world.CreateLocator().Locate().ProtonBuilds.ToDictionary(build => build.Name);

        builds["NoScript"].Problem.Should().Contain("'proton' script is missing").And.Contain(noScript);
        builds["NoManifest"].Problem.Should().Contain("compatibilitytool.vdf or toolmanifest.vdf").And.Contain(noManifest);
        builds["NoFiles"].Problem.Should().Contain("Wine files").And.Contain(noFiles);
    }

    [Fact]
    public void Locate_AcceptsAToolManifestInPlaceOfACompatibilityToolFile()
    {
        using var world = new RuntimeWorld();
        var directory = world.CreateProton("Valve-Style", manifest: false);
        File.WriteAllText(Path.Combine(directory, "toolmanifest.vdf"), "\"manifest\" {}");

        world.CreateLocator().Locate().ProtonBuilds.Should().ContainSingle().Which.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Locate_SkipsFoldersThatAreNotProtonBuilds()
    {
        using var world = new RuntimeWorld();
        world.CreateProton("GE-Proton11-1");
        Directory.CreateDirectory(Path.Combine(world.ToolsRoot, "Some-Random-Folder"));
        Directory.CreateDirectory(Path.Combine(world.ToolsRoot, "SteamLinuxRuntime_soldier"));
        world.CreateProton("SteamLinuxRuntime_sniper");
        world.CreateProton("Proton-LegacyRuntime");

        world.CreateLocator().Locate().ProtonBuilds.Select(build => build.Name).Should().Equal("GE-Proton11-1");
    }

    [Fact]
    public void Locate_ReadsTheVersionLabelAfterTheTimestamp()
    {
        using var world = new RuntimeWorld();
        world.CreateProton("GE-Proton11-1", version: "1762000000 GE-Proton11-1-custom\n");
        world.CreateProton("Bare", version: "just-a-label");

        var builds = world.CreateLocator().Locate().ProtonBuilds.ToDictionary(build => build.Name);

        builds["GE-Proton11-1"].Version.Should().Be("GE-Proton11-1-custom");
        builds["Bare"].Version.Should().Be("just-a-label");
    }

    [Fact]
    public void Locate_FindsSteamsOwnProtonFoldersButNotGames()
    {
        using var world = new RuntimeWorld();
        var common = Path.Combine(world.SteamRoot, "steamapps", "common");
        world.CreateProton("Proton 9.0 (Beta)", common);
        world.CreateProton("Some Game", common);

        world.CreateLocator().Locate().ProtonBuilds.Select(build => build.Name).Should().Equal("Proton 9.0 (Beta)");
    }

    [Fact]
    public void Locate_SearchesTheFlatpakAndTheUserAddedRoots()
    {
        using var world = new RuntimeWorld();
        world.CreateProton(
            "Flatpak-Proton",
            Path.Combine(world.Home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "compatibilitytools.d")
        );
        var extra = world.Combine("my-protons");
        world.CreateProton("Mine-1", extra);

        var inventory = world
            .CreateLocator()
            .Locate(new RuntimeSearchOptions { ExtraProtonRoots = [extra, world.Combine("missing-root")] });

        inventory.ProtonBuilds.Select(build => build.Name).Should().BeEquivalentTo("Flatpak-Proton", "Mine-1");
        inventory.SearchedRoots.Should().Contain(extra).And.Contain(world.Combine("missing-root"));
    }

    [Fact]
    public void Locate_AcceptsAUserRootThatIsItselfABuild()
    {
        using var world = new RuntimeWorld();
        var build = world.CreateProton("Direct", world.Combine("elsewhere"));

        var inventory = world.CreateLocator().Locate(new RuntimeSearchOptions { ExtraProtonRoots = [build] });

        inventory.ProtonBuilds.Should().ContainSingle().Which.Name.Should().Be("Direct");
    }

    [LinuxFact]
    public void Locate_ListsABuildOnceWhenTwoRootsReachItThroughALink()
    {
        using var world = new RuntimeWorld();
        world.CreateProton("GE-Proton11-1");
        var steamLink = Path.Combine(world.Home, ".steam", "steam");
        Directory.CreateDirectory(Path.GetDirectoryName(steamLink)!);
        Directory.CreateSymbolicLink(steamLink, world.SteamRoot);

        var builds = world.CreateLocator().Locate().ProtonBuilds;

        builds.Should().ContainSingle();
        builds[0].Path.Should().Be(Path.Combine(world.ToolsRoot, "GE-Proton11-1"), "the real location wins over the linked spelling");
    }

    [Fact]
    public void Locate_FindsUmuAndTheHelperToolsAndGameMode()
    {
        using var world = new RuntimeWorld();
        var finder = new FakeFinder(
            ("umu-run", "/x/umu-run"),
            ("wine", "/x/wine"),
            ("winetricks", "/x/winetricks"),
            ("gamemoderun", "/x/gamemoderun")
        );

        var inventory = world.CreateLocator(finder: finder, names: new FakeProcessNames("bash", "gamemoded")).Locate();

        inventory.UmuRun.Should().Be("/x/umu-run");
        inventory.Wine.Should().Be("/x/wine");
        inventory.Winetricks.Should().Be("/x/winetricks");
        inventory.GameMode.Should().Be(new GameModeStatus(true, true));
        inventory.GameMode.InstalledButNotRunning.Should().BeFalse();
    }

    [Fact]
    public void Locate_FlagsGameModeThatIsInstalledButNotRunning()
    {
        using var world = new RuntimeWorld();

        var inventory = world.CreateLocator(finder: new FakeFinder(("gamemoderun", "/x/gamemoderun"))).Locate();

        inventory.GameMode.InstalledButNotRunning.Should().BeTrue();
    }

    [Fact]
    public void Locate_ReportsNothingWhenNothingIsInstalled()
    {
        using var world = new RuntimeWorld();

        var inventory = world.CreateLocator().Locate();

        inventory.ProtonBuilds.Should().BeEmpty();
        inventory.UmuRun.Should().BeNull();
        inventory.Wine.Should().BeNull();
        inventory.GameMode.Should().Be(new GameModeStatus(false, false));
    }

    [Fact]
    public void FindUmuRun_PrefersTheUsersPickThenPathThenTheFallbacks()
    {
        using var world = new RuntimeWorld();
        var picked = world.Combine("picked-umu");
        File.WriteAllText(picked, "");
        var fallback = world.Combine("fallback-umu");
        File.WriteAllText(fallback, "");

        var onPath = world.CreateLocator(finder: new FakeFinder(("umu-run", "/on/path")));
        onPath.FindUmuRun(new RuntimeSearchOptions { UmuRunPath = picked }).Should().Be(picked);
        onPath.FindUmuRun(new RuntimeSearchOptions { UmuRunPath = world.Combine("gone") }).Should().Be("/on/path");

        var withFallback = new YuiTushonkaLauncher.Infrastructure.Runtime.RuntimeLocator(
            new FakePlatform(true),
            new FakeFinder(),
            new FakeProcessNames(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<YuiTushonkaLauncher.Infrastructure.Runtime.RuntimeLocator>.Instance,
            world.Home,
            [],
            [world.Combine("absent"), fallback]
        );
        withFallback.FindUmuRun().Should().Be(fallback);
    }

    [Fact]
    public void Locate_OnWindowsSearchesNothingAndReportsUnsupported()
    {
        using var world = new RuntimeWorld();
        world.CreateProton("GE-Proton11-1");
        var finder = new FakeFinder(("umu-run", "/x/umu-run"), ("wine", "/x/wine"));

        var locator = world.CreateLocator(new FakePlatform(false), finder);
        var inventory = locator.Locate();

        inventory.IsSupported.Should().BeFalse();
        inventory.ProtonBuilds.Should().BeEmpty();
        inventory.UmuRun.Should().BeNull();
        inventory.SearchedRoots.Should().BeEmpty();
        locator.FindUmuRun().Should().BeNull();
    }
}
