using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Localization;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Tests.Mods;
using YuiTushonkaLauncher.Tests.Ui;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>The phase's acceptance flow: create an instance, install a mod, launch, read the log.</summary>
[Collection(AvaloniaCollection.Name)]
public sealed class FlowTests
{
    [Fact]
    public async Task CreateInstallLaunchAndSeeALogLine()
    {
        // The launch is faked at the service boundary: a real one needs Proton and a real game. The panel, its state
        // machine and the log page are the real ones.
        FakeLaunch? launch = null;
        await using var world = await UiWorld.CreateAsync(configure: services =>
        {
            launch = new FakeLaunch();
            services.AddSingleton<ILaunchService>(launch);
        });

        // 1. Create: the dialog is filled the way the window would be, then its create command runs.
        world.Dialogs.OnCreate = async dialog =>
        {
            dialog.Name = "flow";
            dialog.SourcePath = world.BaseInstallRoot;
            dialog.DestinationRoot = world.InstancesRoot;
            await dialog.PlanCommand.ExecuteAsync(null);
            dialog.Summary.Should().Contain("files", "the plan is shown before anything is created");
            await dialog.CreateCommand.ExecuteAsync(null);
            return dialog.Instance;
        };

        await world.Shell.InstancesPage.CreateCommand.ExecuteAsync(null);
        world.Shell.InstancesPage.Instances.Should().ContainSingle().Which.Name.Should().Be("flow");
        world.Shell.Instances.ActiveId.Should().NotBeNull("the instance just created becomes active");

        // 2. Install a mod through the real installer, from a real archive.
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/ClientMod.dll", "ClientMod.dll");
        world.Dialogs.NextPath = archive.Path;
        world.Shell.Navigate(LauncherSection.Mods);
        await world.Shell.ModsPage.InstallFromFileCommand.ExecuteAsync(null);

        world.Shell.ModsPage.Error.Should().BeEmpty();
        world
            .Shell.ModsPage.Mods.Should()
            .ContainSingle(
                "status was '{0}', error '{1}', installed {2}",
                world.Shell.ModsPage.Status,
                world.Shell.ModsPage.Error,
                world.Shell.Instances.Active!.InstalledMods.Count
            )
            .Which.Name.Should()
            .Be("Fixture Client Mod");

        // 3. Launch: the panel walks the state machine and ends Running.
        launch!.InstanceId = world.Shell.Instances.ActiveId!.Value;
        world.Shell.LaunchPanel.ProfileUsername = "yui";
        await world.Shell.LaunchPanel.LaunchCommand.ExecuteAsync(null);

        world.Shell.LaunchPanel.Session.State.Should().Be(LaunchButtonState.Running);
        world.Shell.LaunchPanel.CanPressStop.Should().BeTrue();
        world.Shell.LaunchPanel.CanPressLaunch.Should().BeTrue("a running pair can still be launched again from idle words");
        launch
            .Steps.Should()
            .Equal(
                LaunchStep.PreFlight,
                LaunchStep.PatchCheck,
                LaunchStep.ServerStart,
                LaunchStep.Ready,
                LaunchStep.GameStart,
                LaunchStep.Detached
            );

        // 4. The log page shows the line the launch produced.
        var instance = world.Shell.Instances.Active!;
        var runDirectory = System.IO.Path.Join(instance.Path, "Logs", "run-1");
        System.IO.Directory.CreateDirectory(runDirectory);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Join(runDirectory, "application.log"), "[Info] game started\n");

        world.Shell.Navigate(LauncherSection.Logs);
        world.Shell.LogsPage.Sources.Should().Contain(source => source.Kind == LogSourceKind.Game);
        world.Shell.LogsPage.Lines.Should().Contain("[Info] game started");
    }

    [Fact]
    public async Task ABusyPageRefusesASecondOperation()
    {
        await using var world = await UiWorld.CreateAsync();
        await world.SeedInstanceAsync("busy");
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/Busy.dll", "ClientMod.dll");
        world.Dialogs.NextPath = archive.Path;
        var page = world.Shell.ModsPage;
        await page.LoadAsync(default);

        // An operation is already in flight (an install or an uninstall holds the flag); a second one must not start.
        page.IsBusy = true;
        await page.InstallFromFileCommand.ExecuteAsync(null);

        world.Dialogs.Asked.Should().Contain(entry => entry.StartsWith("file:", StringComparison.Ordinal));
        page.Mods.Should().BeEmpty("the install never ran, so nothing was written");
        page.Status.Should().BeEmpty();
        page.Error.Should().BeEmpty("nothing failed: the second command simply did not run");
    }

    [Fact]
    public async Task TheFinalStateComesFromTheResultNotFromProgressReports()
    {
        // Progress<T> posts, so the button must reach Running from the pipeline's result alone; otherwise a slow dispatch
        // leaves it on "waiting for the server" after the pair is already up.
        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<ILaunchService>(new SilentLaunch()));
        var instance = await world.SeedInstanceAsync("silent");
        world.Shell.Navigate(LauncherSection.Mods);
        await world.Shell.LoadAsync(default);

        await world.Shell.LaunchPanel.LaunchCommand.ExecuteAsync(null);

        world.Shell.LaunchPanel.Session.State.Should().Be(LaunchButtonState.Running);
        world.Shell.LaunchPanel.CanPressStop.Should().BeTrue();
        world.Shell.HasRunningProcesses().Should().BeTrue();
        _ = instance;
    }
}

/// <summary>A launch service that reports nothing at all, only the result.</summary>
public sealed class SilentLaunch : ILaunchService
{
    public Task<LaunchPreflight> PlanAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LaunchPreflight { InstanceId = request.InstanceId, InstanceName = "silent" });
    }

    public Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            new LaunchResult
            {
                InstanceId = request.InstanceId,
                Succeeded = true,
                ServerProcessId = 1,
                GameProcessId = 2,
            }
        );
    }
}

/// <summary>A launch service that reports the pipeline's steps and creates nothing.</summary>
public sealed class FakeLaunch : ILaunchService
{
    public Guid InstanceId { get; set; }

    public List<LaunchStep> Steps { get; } = [];

    public Task<LaunchPreflight> PlanAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LaunchPreflight { InstanceId = request.InstanceId, InstanceName = "flow" });
    }

    public Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var steps = new[]
        {
            LaunchStep.PreFlight,
            LaunchStep.PatchCheck,
            LaunchStep.ServerStart,
            LaunchStep.Ready,
            LaunchStep.GameStart,
            LaunchStep.Detached,
        };

        foreach (var step in steps)
        {
            Steps.Add(step);
            progress?.Report(new LaunchProgress(step, LaunchStepOutcome.Succeeded, step.ToString()));
        }

        return Task.FromResult(
            new LaunchResult
            {
                InstanceId = InstanceId,
                Succeeded = true,
                ServerProcessId = 11,
                GameProcessId = 12,
                ProfileId = "p",
            }
        );
    }
}
