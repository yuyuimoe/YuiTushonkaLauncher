using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Tests.Instances;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The window the launcher opens on lists instances and nothing else, and the manager is a separate window opened from
/// the details pane — the shape the reference launcher uses.
/// </summary>
public sealed class HomeTests
{
    [Fact]
    public async Task Manage_OpensTheManagerWindowOnTheSelectedInstancesMods()
    {
        var windows = new FakeWindowController();
        await using var world = await UiWorld.CreateAsync(configure: services =>
            services.AddSingleton<YuiTushonkaLauncher.ViewModels.IWindowController>(windows)
        );
        await world.SeedInstanceAsync("home-manage");
        var home = world.Get<HomeViewModel>();
        await home.LoadAsync(CancellationToken.None);

        home.Instances.Selected.Should().NotBeNull();
        home.HasSelection.Should().BeTrue();

        home.ManageCommand.Execute(null);

        windows.Managers.Should().ContainSingle().Which.Should().Be(LauncherSection.Mods);
    }

    [Fact]
    public async Task Manage_DoesNothingWhenNoInstanceIsSelected()
    {
        var windows = new FakeWindowController();
        await using var world = await UiWorld.CreateAsync(configure: services =>
            services.AddSingleton<YuiTushonkaLauncher.ViewModels.IWindowController>(windows)
        );
        var home = world.Get<HomeViewModel>();
        await home.LoadAsync(CancellationToken.None);

        home.ManageCommand.Execute(null);

        windows.Managers.Should().BeEmpty();
    }

    [Fact]
    public async Task Settings_OpensTheManagerOnTheSettingsSection()
    {
        var windows = new FakeWindowController();
        await using var world = await UiWorld.CreateAsync(configure: services =>
            services.AddSingleton<YuiTushonkaLauncher.ViewModels.IWindowController>(windows)
        );
        var home = world.Get<HomeViewModel>();
        await home.LoadAsync(CancellationToken.None);

        home.OpenSettingsCommand.Execute(null);

        windows.Managers.Should().ContainSingle().Which.Should().Be(LauncherSection.Settings);
    }

    [Fact]
    public async Task Adopt_HidesTheDestinationAndKeepsTheInstallWhereItIs()
    {
        await using var world = await UiWorld.CreateAsync();
        var temp = new Infrastructure.TempDirectory();
        var installRoot = temp.Combine("adopted-home");
        InstallDetectorTests.WriteMergedInstall(temp, installRoot);

        var page = world.Instances;
        await page.LoadAsync(CancellationToken.None);
        world.Dialogs.OnCreate = async dialog =>
        {
            dialog.IsAdopting.Should().BeTrue();
            dialog.ShowsDestination.Should().BeFalse("adopting never asks where to put a copy");
            dialog.ShowsAdoptOptions.Should().BeTrue();
            dialog.Name = "adopted-home";
            dialog.SourcePath = installRoot;
            dialog.ReferencePath = world.BaseInstallRoot;
            dialog.BaseRoot = world.InstancesRoot;
            await dialog.DetectCommand.ExecuteAsync(null);
            dialog.HasVersionMismatch.Should().BeFalse();
            await dialog.CreateCommand.ExecuteAsync(null);
            return dialog.Instance;
        };

        await page.AdoptCommand.ExecuteAsync(null);

        var adopted = world.Get<YuiTushonkaLauncher.Core.Instances.IInstanceService>();
        var stored = await adopted.ListAsync();
        stored.Should().ContainSingle(instance => instance.Path == Path.GetFullPath(installRoot));
        temp.Dispose();
    }

    [Fact]
    public async Task Adopt_RefusesAGameBuildTheSptReleaseDoesNotWant()
    {
        await using var world = await UiWorld.CreateAsync();
        var temp = new Infrastructure.TempDirectory();
        var installRoot = temp.Combine("wrong-build");
        InstallDetectorTests.WriteMergedInstall(temp, installRoot);
        var core = Path.Combine(installRoot, "SPT_Runtime", "SPT_Data", "configs", "core.json");
        File.WriteAllText(core, """{ "compatibleTarkovVersion": "0.16.9.40087" }""");

        var page = world.Instances;
        await page.LoadAsync(CancellationToken.None);
        world.Dialogs.OnCreate = async dialog =>
        {
            dialog.IsAdopting = true;
            dialog.SourcePath = installRoot;
            await dialog.DetectCommand.ExecuteAsync(null);
            dialog.HasVersionMismatch.Should().BeTrue();
            await dialog.CreateCommand.ExecuteAsync(null);
            return dialog.Instance;
        };

        await page.AdoptCommand.ExecuteAsync(null);

        var service = world.Get<YuiTushonkaLauncher.Core.Instances.IInstanceService>();
        (await service.ListAsync()).Should().BeEmpty();
        world.Dialogs.CreateDialogs.Should().ContainSingle().Which.Error.Should().Contain("0.16.9.40087");
        temp.Dispose();
    }
}
