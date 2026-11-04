using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Tests.Runtime;

public sealed class RuntimeLauncherTests
{
    [Fact]
    public void Plan_WithUmuRunsTheGameThroughUmuWithTheDocumentedVariables()
    {
        using var world = new RuntimeWorld();
        var proton = world.CreateProton("GE-Proton11-1");
        var prefix = world.CreatePrefix();
        var profile = world.ProtonProfile(proton, row => row.PrefixPath = prefix);

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeTrue();
        plan.Problems.Should().BeEmpty();
        plan.PrefixPath.Should().Be(prefix);
        plan.Command!.FileName.Should().Be(RuntimeWorld.UmuPath);
        plan.Command.Arguments.Should().Equal(world.Game, "-force-gfx-jobs", "native");
        plan.Command.WorkingDirectory.Should().Be(world.GameDirectory);
        plan.Command.Environment.Should()
            .Contain("WINEPREFIX", prefix)
            .And.Contain("PROTONPATH", proton)
            .And.Contain("GAMEID", "umu-default")
            .And.Contain("STORE", "none");
        plan.Command.Environment.Should().Contain("WINEDLLOVERRIDES", "winhttp=n,b");
    }

    [Fact]
    public void Plan_AcceptsTheProtonScriptPathAsWellAsTheBuildFolder()
    {
        using var world = new RuntimeWorld();
        var proton = world.CreateProton("GE-Proton11-1");
        var profile = world.ProtonProfile(Path.Combine(proton, "proton"), row => row.PrefixPath = world.CreatePrefix());

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.Command!.Environment["PROTONPATH"].Should().Be(proton);
    }

    [Fact]
    public void Plan_PutsThePrefixInsideTheInstanceWhenTheProfileNamesNone()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(world.CreateProton("GE-Proton11-1"));
        var launcher = world.CreateLauncher();

        launcher.ResolvePrefix(world.Instance, profile).Should().Be(Path.Combine(world.InstanceRoot, "wineprefix"));
        launcher
            .Plan(world.Instance, profile, world.GameRequest())
            .Command!.Environment["WINEPREFIX"]
            .Should()
            .Be(Path.Combine(world.InstanceRoot, "wineprefix"));
    }

    [Fact]
    public void Plan_WarnsThatAMissingPrefixWillBeCreatedButDoesNotBlock()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(world.CreateProton("GE-Proton11-1"));

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeTrue();
        plan.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.PrefixNotCreated);
    }

    [Fact]
    public void Plan_WithoutUmuFallsBackToProtonItselfAndExplainsWhy()
    {
        using var world = new RuntimeWorld();
        var proton = world.CreateProton("GE-Proton11-1");
        var prefix = world.CreatePrefix();
        var launcher = world.CreateLauncher(finder: new FakeFinder(("python3", "/fake/bin/python3")));

        var plan = launcher.Plan(world.Instance, world.ProtonProfile(proton, row => row.PrefixPath = prefix), world.GameRequest());

        plan.CanRun.Should().BeTrue();
        plan.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.UmuMissing);
        plan.Command!.FileName.Should().Be(Path.Combine(proton, "proton"));
        plan.Command.Arguments.Should().Equal("run", world.Game, "-force-gfx-jobs", "native");
        plan.Command.Environment.Should()
            .Contain("STEAM_COMPAT_DATA_PATH", prefix)
            .And.Contain("STEAM_COMPAT_CLIENT_INSTALL_PATH", world.SteamRoot);
        plan.Command.Environment.Should().NotContainKey("PROTONPATH");
    }

    [Fact]
    public void Plan_WithoutUmuAndWithoutPythonIsAnErrorBecauseProtonCannotRun()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(world.CreateProton("GE-Proton11-1"), row => row.PrefixPath = world.CreatePrefix());

        var plan = world.CreateLauncher(finder: new FakeFinder()).Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeFalse();
        plan.Command.Should().BeNull();
        plan.Errors.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.PythonMissing);
    }

    [Fact]
    public void Plan_WarnsWhenTheSteamFolderOfADirectProtonCannotBeInferred()
    {
        using var world = new RuntimeWorld();
        var proton = world.CreateProton("Loose", world.Combine("loose-root"));
        var profile = world.ProtonProfile(proton, row => row.PrefixPath = world.CreatePrefix());

        var plan = world.CreateLauncher(finder: new FakeFinder(("python3", "/p"))).Plan(world.Instance, profile, world.GameRequest());

        plan.Problems.Select(problem => problem.Code).Should().Contain(RuntimeProblemCode.SteamRootUnknown);
        plan.Command!.Environment.Should().NotContainKey("STEAM_COMPAT_CLIENT_INSTALL_PATH");
    }

    [Fact]
    public void Plan_InfersTheSteamFolderOfAValveBuild()
    {
        using var world = new RuntimeWorld();
        var proton = world.CreateProton("Proton 9.0 (Beta)", Path.Combine(world.SteamRoot, "steamapps", "common"));
        var profile = world.ProtonProfile(proton, row => row.PrefixPath = world.CreatePrefix());

        var plan = world.CreateLauncher(finder: new FakeFinder(("python3", "/p"))).Plan(world.Instance, profile, world.GameRequest());

        plan.Command!.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"].Should().Be(world.SteamRoot);
    }

    [Fact]
    public void Plan_ReportsAMissingProtonBuildWithItsExactPath()
    {
        using var world = new RuntimeWorld();
        var missing = world.Combine("no-such-proton");

        var plan = world.CreateLauncher().Plan(world.Instance, world.ProtonProfile(missing), world.GameRequest());

        plan.CanRun.Should().BeFalse();
        var error = plan.Errors.Should().ContainSingle().Subject;
        error.Code.Should().Be(RuntimeProblemCode.ProtonMissing);
        error.Path.Should().Be(missing);
        error.Message.Should().Contain(missing);
    }

    [Fact]
    public void Plan_ReportsAnInvalidProtonBuildAndAProfileThatNamesNone()
    {
        using var world = new RuntimeWorld();
        var broken = world.CreateProton("Broken", script: false);
        var launcher = world.CreateLauncher();

        launcher
            .Plan(world.Instance, world.ProtonProfile(broken), world.GameRequest())
            .Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be(RuntimeProblemCode.ProtonInvalid);
        launcher
            .Plan(world.Instance, world.ProtonProfile(" "), world.GameRequest())
            .Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be(RuntimeProblemCode.ProtonNotConfigured);
    }

    [Fact]
    public void Plan_WithWineRunsTheBinaryWithThePrefix()
    {
        using var world = new RuntimeWorld();
        var wine = world.Combine("bin", "wine");
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        File.WriteAllText(wine, "");
        var prefix = world.CreatePrefix();
        var profile = new RuntimeProfile
        {
            Id = Guid.NewGuid(),
            Name = "wine",
            Kind = RuntimeKind.Wine,
            Executable = wine,
            PrefixPath = prefix,
            Environment = new Dictionary<string, string> { ["WINEDLLOVERRIDES"] = "winhttp=n,b" },
        };

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.Command!.FileName.Should().Be(wine);
        plan.Command.Arguments.Should().Equal(world.Game, "-force-gfx-jobs", "native");
        plan.Command.Environment.Should().Contain("WINEPREFIX", prefix);
        plan.Command.Environment.Should().NotContainKey("PROTONPATH");
    }

    [Fact]
    public void Plan_WithWineFindsTheBinaryOnPathOrReportsItAbsent()
    {
        using var world = new RuntimeWorld();
        var profile = new RuntimeProfile
        {
            Id = Guid.NewGuid(),
            Name = "wine",
            Kind = RuntimeKind.Wine,
            PrefixPath = world.CreatePrefix(),
        };

        var found = world
            .CreateLauncher(finder: new FakeFinder(("wine", "/usr/bin/wine")))
            .Plan(world.Instance, profile, world.GameRequest(doorstop: false));
        var absent = world.CreateLauncher(finder: new FakeFinder()).Plan(world.Instance, profile, world.GameRequest(doorstop: false));

        found.Command!.FileName.Should().Be("/usr/bin/wine");
        absent.Errors.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.WineMissing);
        profile.Executable = world.Combine("ghost-wine");
        world
            .CreateLauncher()
            .Plan(world.Instance, profile, world.GameRequest(doorstop: false))
            .Errors.Single()
            .Path.Should()
            .Be(world.Combine("ghost-wine"));
    }

    [Fact]
    public void Plan_WithoutAProfileOnLinuxRefusesWithAnExplanation()
    {
        using var world = new RuntimeWorld();

        var plan = world.CreateLauncher().Plan(world.Instance, null, world.GameRequest());

        plan.CanRun.Should().BeFalse();
        plan.Errors.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.NoProfile);
    }

    [Fact]
    public void Plan_RefusesANativeProfileForAWindowsProgramOnLinux()
    {
        using var world = new RuntimeWorld();
        var profile = new RuntimeProfile
        {
            Id = Guid.NewGuid(),
            Name = "native",
            Kind = RuntimeKind.Native,
        };

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.Errors.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.ProfileKindUnsupported);
    }

    [Fact]
    public void Plan_RunsANativeProgramDirectlyEvenWithoutAProfile()
    {
        using var world = new RuntimeWorld();
        var server = Path.Combine(world.InstanceRoot, "SPT.Server.Linux");
        File.WriteAllText(server, "");

        var plan = world
            .CreateLauncher()
            .Plan(
                world.Instance,
                null,
                new RuntimeLaunchRequest
                {
                    Program = server,
                    WorkingDirectory = world.InstanceRoot,
                    Target = ProgramTarget.NativeProgram,
                }
            );

        plan.CanRun.Should().BeTrue();
        plan.PrefixPath.Should().BeNull();
        plan.Command!.FileName.Should().Be(server);
        plan.Command.Environment.Should().BeEmpty();
    }

    [Fact]
    public void Plan_AppliesProfileVariablesAndExtraArgumentsToANativeProgramToo()
    {
        using var world = new RuntimeWorld();
        var server = Path.Combine(world.InstanceRoot, "SPT.Server.Linux");
        File.WriteAllText(server, "");
        var profile = world.ProtonProfile(
            "/unused",
            row =>
            {
                row.Environment = new Dictionary<string, string> { ["DOTNET_GCHeapCount"] = "2" };
                row.ExtraArguments = "--verbose";
            }
        );

        var plan = world
            .CreateLauncher()
            .Plan(
                world.Instance,
                profile,
                new RuntimeLaunchRequest
                {
                    Program = server,
                    WorkingDirectory = world.InstanceRoot,
                    Target = ProgramTarget.NativeProgram,
                }
            );

        plan.Command!.Environment.Should().Contain("DOTNET_GCHeapCount", "2");
        plan.Command.Arguments.Should().Equal("--verbose");
    }

    [Fact]
    public void Plan_OnWindowsIsNativeAndChecksNothingLinuxSpecific()
    {
        using var world = new RuntimeWorld();
        var launcher = world.CreateLauncher(new FakePlatform(false), new FakeFinder());

        var plan = launcher.Plan(world.Instance, null, world.GameRequest());

        plan.CanRun.Should().BeTrue();
        plan.Problems.Should().BeEmpty();
        plan.PrefixPath.Should().BeNull();
        plan.Command!.FileName.Should().Be(world.Game);
        plan.Command.Arguments.Should().Equal("-force-gfx-jobs", "native");
        plan.Command.Environment.Should().BeEmpty();
        Directory.Exists(Path.Combine(world.InstanceRoot, "wineprefix")).Should().BeFalse();
    }

    [Fact]
    public void Plan_OnWindowsIgnoresALinuxProfileAndNeverMentionsProtonOrGameMode()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile("/does/not/exist", row => row.UseGameMode = true);
        var launcher = world.CreateLauncher(new FakePlatform(false), new FakeFinder(("gamemoderun", "/gm")));

        var plan = launcher.Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeTrue();
        plan.Command!.FileName.Should().Be(world.Game);
        plan.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Plan_MergesProfileVariablesWithTheOnesInTheExtraArgumentsLine()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(
            world.CreateProton("GE-Proton11-1"),
            row =>
            {
                row.PrefixPath = world.CreatePrefix();
                row.Environment = new Dictionary<string, string> { ["WINEDLLOVERRIDES"] = "winhttp=n,b", ["DXVK_HUD"] = "fps" };
                row.ExtraArguments = "DXVK_HUD=full MANGOHUD=1 --disable-software-renderer";
            }
        );

        var command = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest()).Command!;

        command.Environment.Should().Contain("DXVK_HUD", "full").And.Contain("MANGOHUD", "1");
        command.Arguments.Should().Equal(world.Game, "-force-gfx-jobs", "native", "--disable-software-renderer");
    }

    [Fact]
    public void Plan_RejectsAProfileThatOverridesAVariableTheLauncherOwns()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(
            world.CreateProton("GE-Proton11-1"),
            row =>
            {
                row.PrefixPath = world.CreatePrefix();
                row.Environment = new Dictionary<string, string> { ["WINEPREFIX"] = "/elsewhere", ["WINEDLLOVERRIDES"] = "winhttp=n,b" };
            }
        );

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeFalse();
        plan.Errors.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.EnvironmentInvalid);
    }

    [Fact]
    public void Plan_WarnsWhenTheBepInExDllOverrideIsMissingButOnlyForTheGame()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(
            world.CreateProton("GE-Proton11-1"),
            row =>
            {
                row.PrefixPath = world.CreatePrefix();
                row.Environment = new Dictionary<string, string>();
            }
        );
        var launcher = world.CreateLauncher();

        var game = launcher.Plan(world.Instance, profile, world.GameRequest(doorstop: true));
        var other = launcher.Plan(world.Instance, profile, world.GameRequest(doorstop: false));

        game.CanRun.Should().BeTrue();
        game.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.DllOverrideMissing);
        other.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ReportsAMissingProgramAndWorkingDirectoryWithTheirPaths()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(world.CreateProton("GE-Proton11-1"), row => row.PrefixPath = world.CreatePrefix());

        var plan = world
            .CreateLauncher()
            .Plan(
                world.Instance,
                profile,
                new RuntimeLaunchRequest { Program = world.Combine("nope.exe"), WorkingDirectory = world.Combine("nowhere") }
            );

        plan.CanRun.Should().BeFalse();
        plan.Errors.Select(error => error.Code)
            .Should()
            .BeEquivalentTo([RuntimeProblemCode.ProgramMissing, RuntimeProblemCode.WorkingDirectoryMissing]);
        plan.Errors.Select(error => error.Path).Should().BeEquivalentTo([world.Combine("nope.exe"), world.Combine("nowhere")]);
    }

    [Fact]
    public void Plan_RefusesAPrefixThatIsAFolderOfSomeoneElsesFiles()
    {
        using var world = new RuntimeWorld();
        var foreign = world.Combine("foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "notes.txt"), "precious");
        var profile = world.ProtonProfile(world.CreateProton("GE-Proton11-1"), row => row.PrefixPath = foreign);

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeFalse();
        var error = plan.Errors.Should().ContainSingle().Subject;
        error.Code.Should().Be(RuntimeProblemCode.PrefixBroken);
        error.Path.Should().Be(foreign);
        File.ReadAllText(Path.Combine(foreign, "notes.txt")).Should().Be("precious");
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void Plan_NamesTheFolderThatMakesAPrefixUnwritable()
    {
        if (Environment.UserName == "root")
        {
            return;
        }

        using var world = new RuntimeWorld();
        var locked = world.Combine("locked");
        Directory.CreateDirectory(locked);
        RuntimeWorld.Lock(locked);

        try
        {
            var profile = world.ProtonProfile(
                world.CreateProton("GE-Proton11-1"),
                row => row.PrefixPath = Path.Combine(locked, "deeper", "prefix")
            );

            var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

            plan.CanRun.Should().BeFalse();
            var error = plan.Errors.Should().ContainSingle().Subject;
            error.Code.Should().Be(RuntimeProblemCode.PrefixNotWritable);
            error.Path.Should().Be(locked);
            Directory.Exists(Path.Combine(locked, "deeper")).Should().BeFalse("planning must not create anything");
        }
        finally
        {
            RuntimeWorld.Unlock(locked);
        }
    }

    [Fact]
    public void Plan_WrapsTheCommandInGameModeWhenAskedAndInstalled()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(
            world.CreateProton("GE-Proton11-1"),
            row =>
            {
                row.PrefixPath = world.CreatePrefix();
                row.UseGameMode = true;
            }
        );
        var finder = new FakeFinder(("umu-run", RuntimeWorld.UmuPath), ("gamemoderun", "/usr/bin/gamemoderun"));

        var plan = world
            .CreateLauncher(finder: finder, names: new FakeProcessNames("gamemoded"))
            .Plan(world.Instance, profile, world.GameRequest());

        plan.Problems.Should().BeEmpty();
        plan.Command!.FileName.Should().Be("/usr/bin/gamemoderun");
        plan.Command.Arguments.Take(2).Should().Equal(RuntimeWorld.UmuPath, world.Game);
    }

    [Fact]
    public void Plan_WarnsWhenGameModeIsInstalledButItsDaemonIsNotRunning()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(
            world.CreateProton("GE-Proton11-1"),
            row =>
            {
                row.PrefixPath = world.CreatePrefix();
                row.UseGameMode = true;
            }
        );
        var finder = new FakeFinder(("umu-run", RuntimeWorld.UmuPath), ("gamemoderun", "/usr/bin/gamemoderun"));

        var plan = world.CreateLauncher(finder: finder).Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeTrue();
        plan.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.GameModeNotRunning);
        plan.Command!.FileName.Should().Be("/usr/bin/gamemoderun");
    }

    [Fact]
    public void Plan_RunsWithoutGameModeButWarnsWhenItIsRequestedAndAbsent()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(
            world.CreateProton("GE-Proton11-1"),
            row =>
            {
                row.PrefixPath = world.CreatePrefix();
                row.UseGameMode = true;
            }
        );

        var plan = world.CreateLauncher().Plan(world.Instance, profile, world.GameRequest());

        plan.CanRun.Should().BeTrue();
        plan.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.GameModeMissing);
        plan.Command!.FileName.Should().Be(RuntimeWorld.UmuPath);
    }

    [Fact]
    public void PlanTool_RunsEachHelperThroughUmu()
    {
        using var world = new RuntimeWorld();
        var profile = world.ProtonProfile(world.CreateProton("GE-Proton11-1"), row => row.PrefixPath = world.CreatePrefix());
        var launcher = world.CreateLauncher();

        Verb(PrefixTool.Initialize).Should().Equal("wineboot", "-u");
        Verb(PrefixTool.WineConfig).Should().Equal("winecfg");
        Verb(PrefixTool.RegistryEditor).Should().Equal("regedit");
        Verb(PrefixTool.Winetricks, "-q", "dotnetdesktop9").Should().Equal("winetricks", "-q", "dotnetdesktop9");

        string[] Verb(PrefixTool tool, params string[] arguments)
        {
            var plan = launcher.PlanTool(world.Instance, profile, new RuntimeToolRequest { Tool = tool, Arguments = arguments });
            plan.CanRun.Should().BeTrue();
            plan.Command!.FileName.Should().Be(RuntimeWorld.UmuPath);
            plan.Command.Environment.Should().ContainKey("WINEPREFIX").And.ContainKey("PROTONPATH");
            return plan.Command.Arguments.ToArray();
        }
    }

    [Fact]
    public void PlanTool_InitializeToleratesAMissingPrefixAndARepairablePartialOne()
    {
        using var world = new RuntimeWorld();
        var partial = world.Combine("partial");
        Directory.CreateDirectory(Path.Combine(partial, "drive_c"));
        var proton = world.CreateProton("GE-Proton11-1");
        var launcher = world.CreateLauncher();
        var request = new RuntimeToolRequest { Tool = PrefixTool.Initialize };

        launcher.PlanTool(world.Instance, world.ProtonProfile(proton), request).Problems.Should().BeEmpty();
        launcher.PlanTool(world.Instance, world.ProtonProfile(proton, row => row.PrefixPath = partial), request).CanRun.Should().BeTrue();
        launcher
            .PlanTool(
                world.Instance,
                world.ProtonProfile(proton, row => row.PrefixPath = partial),
                new RuntimeToolRequest { Tool = PrefixTool.WineConfig }
            )
            .CanRun.Should()
            .BeFalse("only wineboot may repair a partial prefix");
    }

    [Fact]
    public void PlanTool_WithoutUmuRunsProtonDirectlyAndWinetricksAgainstItsWine()
    {
        using var world = new RuntimeWorld();
        var proton = world.CreateProton("GE-Proton11-1");
        var prefix = world.CreatePrefix(inPfx: true);
        var profile = world.ProtonProfile(proton, row => row.PrefixPath = prefix);
        var launcher = world.CreateLauncher(finder: new FakeFinder(("python3", "/p"), ("winetricks", "/usr/bin/winetricks")));

        var boot = launcher.PlanTool(world.Instance, profile, new RuntimeToolRequest { Tool = PrefixTool.Initialize });
        var tricks = launcher.PlanTool(
            world.Instance,
            profile,
            new RuntimeToolRequest { Tool = PrefixTool.Winetricks, Arguments = ["-q", "vcrun2022"] }
        );

        boot.Command!.FileName.Should().Be(Path.Combine(proton, "proton"));
        boot.Command.Arguments.Should().Equal("run", "wineboot", "-u");
        tricks.Command!.FileName.Should().Be("/usr/bin/winetricks");
        tricks
            .Command.Environment.Should()
            .Contain("WINEPREFIX", Path.Combine(prefix, "pfx"))
            .And.Contain("WINE", Path.Combine(proton, "files", "bin", "wine"));
    }

    [Fact]
    public void PlanTool_ReportsMissingWinetricksAndWorksThroughPlainWine()
    {
        using var world = new RuntimeWorld();
        var prefix = world.CreatePrefix();
        var profile = new RuntimeProfile
        {
            Id = Guid.NewGuid(),
            Name = "wine",
            Kind = RuntimeKind.Wine,
            PrefixPath = prefix,
        };

        var without = world.CreateLauncher(finder: new FakeFinder(("wine", "/usr/bin/wine")));
        var with = world.CreateLauncher(finder: new FakeFinder(("wine", "/usr/bin/wine"), ("winetricks", "/usr/bin/winetricks")));
        var tricks = new RuntimeToolRequest { Tool = PrefixTool.Winetricks };

        without
            .PlanTool(world.Instance, profile, tricks)
            .Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be(RuntimeProblemCode.ToolMissing);
        with.PlanTool(world.Instance, profile, tricks)
            .Command!.Environment.Should()
            .Contain("WINE", "/usr/bin/wine")
            .And.Contain("WINEPREFIX", prefix);
        with.PlanTool(world.Instance, profile, new RuntimeToolRequest { Tool = PrefixTool.WineConfig })
            .Command!.Arguments.Should()
            .Equal("winecfg");
    }

    [Fact]
    public void PlanTool_OnWindowsIsRefused()
    {
        using var world = new RuntimeWorld();

        var plan = world
            .CreateLauncher(new FakePlatform(false))
            .PlanTool(world.Instance, null, new RuntimeToolRequest { Tool = PrefixTool.WineConfig });

        plan.CanRun.Should().BeFalse();
        plan.Errors.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.ProfileKindUnsupported);
    }

    [Fact]
    public void Describe_QuotesWhatNeedsQuotingAndSortsTheVariables()
    {
        var command = new LaunchCommand
        {
            FileName = "/usr/bin/umu-run",
            Arguments = ["/games/My Game/a.exe", "plain", ""],
            Environment = new Dictionary<string, string> { ["B"] = "two words", ["A"] = "1" },
        };

        command.Describe().Should().Be("A=1 B=\"two words\" /usr/bin/umu-run \"/games/My Game/a.exe\" plain \"\"");
    }
}
