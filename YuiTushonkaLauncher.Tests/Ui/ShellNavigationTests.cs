using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.ViewModels;
using YuiTushonkaLauncher.Views;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The shell: every section reachable from the rail, every page resolving to a real view, and no dead end anywhere.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class ShellNavigationTests
{
    private static readonly Dictionary<LauncherSection, Type> Expected = new()
    {
        [LauncherSection.Forge] = typeof(ForgePageViewModel),
        [LauncherSection.Mods] = typeof(ModsPageViewModel),
        [LauncherSection.Server] = typeof(ServerPageViewModel),
        [LauncherSection.Logs] = typeof(LogsPageViewModel),
        [LauncherSection.Settings] = typeof(SettingsPageViewModel),
    };

    [Fact]
    public async Task TheRailHasExactlyOneItemPerSection()
    {
        await using var world = await UiWorld.CreateAsync();

        world.Shell.NavItems.Select(item => item.Section).Should().BeEquivalentTo(Expected.Keys);
        world.Shell.NavItems.Should().OnlyHaveUniqueItems(item => item.Section);
    }

    [Fact]
    public async Task EverySectionShowsItsPage_AndSelectsExactlyOneRailItem()
    {
        await using var world = await UiWorld.CreateAsync();

        foreach (var (section, pageType) in Expected)
        {
            world.Shell.Navigate(section);

            world.Shell.CurrentPage.GetType().Should().Be(pageType, "section {0} must show its own page", section);
            world.Shell.SelectedSection.Should().Be(section);
            world.Shell.NavItems.Where(item => item.IsSelected).Should().ContainSingle().Which.Section.Should().Be(section);
        }
    }

    [Fact]
    public async Task EveryPageResolvesToAView_ThroughTheExplicitTemplates()
    {
        await using var world = await UiWorld.CreateAsync();
        var templates = Application.Current!.DataTemplates;
        var pages = new object[]
        {
            world.Shell.InstancesPage,
            world.Shell.ForgePage,
            world.Shell.ModsPage,
            world.Shell.ServerPage,
            world.Shell.LogsPage,
            world.Shell.SettingsPage,
            world.Services.GetService(typeof(PatchPageViewModel))!,
        };

        foreach (var page in pages)
        {
            var template = templates.FirstOrDefault(candidate => candidate.Match(page));
            template.Should().NotBeNull("a view model without a template is a blank panel at runtime");

            var view = template!.Build(page);
            view.Should().BeAssignableTo<Control>();
            view.Should().NotBeOfType<TextBlock>("the reflection view locator is gone; a TextBlock here means a regression");
        }
    }

    [Fact]
    public async Task TheStatusBarNamesTheActiveInstance()
    {
        Headless.EnsureStarted();
        await using var world = await UiWorld.CreateAsync();
        var status = world.Shell.StatusBar;

        status.InstanceText.Should().Be(world.Localizer["status.noInstance"]);
        status.ServerText.Should().Contain(world.Localizer["server.stateOff"]);

        await world.SeedInstanceAsync("first");
        await world.Shell.InstancesPage.LoadAsync(default);

        status.InstanceText.Should().Contain("first");
        world
            .Shell.InstancesPage.Instances.Should()
            .ContainSingle()
            .Which.IsActive.Should()
            .BeTrue("the active instance is marked on its card");
        world.Shell.InstancesPage.Instances.Single().ActiveLabel.Should().Be(world.Localizer["instances.active"]);
    }

    [Fact]
    public async Task TheLaunchPanelIsDisabledWithoutAnInstance_AndEnabledWithOne()
    {
        await using var world = await UiWorld.CreateAsync();
        var launch = world.Shell.LaunchPanel;

        launch.HasInstance.Should().BeFalse();
        launch.CanPressLaunch.Should().BeFalse();

        await world.SeedInstanceAsync("playable");
        world.Shell.Navigate(LauncherSection.Mods);
        await world.Shell.LoadAsync(default);

        launch.HasInstance.Should().BeTrue();
        launch.CanPressLaunch.Should().BeTrue();
        launch.Session.State.Should().Be(YuiTushonkaLauncher.Core.Launch.LaunchButtonState.Idle);
    }
}
