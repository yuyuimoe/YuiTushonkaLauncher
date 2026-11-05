using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Tests.Forge;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The sidebar has three tabs and a browser window has to open with a list: what the user does not ask for must not cost
/// a request, and what the window shows must be fetched on opening.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class ForgeLazyTests
{
    private static ForgeMod Mod(int id, string guid, string version = "1.0.0")
    {
        return new ForgeMod
        {
            Id = id,
            Guid = guid,
            Name = "Mod " + id,
            Slug = $"mod-{id}",
            Teaser = "teaser",
            Versions =
            [
                new ForgeModVersion
                {
                    Id = id * 10,
                    Version = SemanticVersion.Parse(version),
                    SptConstraint = VersionRange.Parse("~4.1"),
                    DownloadUrl = $"https://sp-mod.test/{id}.zip",
                },
            ],
        };
    }

    private static async Task<(UiWorld World, FakeForgeClient Forge, ForgePageViewModel Page)> WorldAsync(params ForgeMod[] mods)
    {
        var forge = new FakeForgeClient();
        foreach (var mod in mods)
        {
            forge.Mods[mod.Guid!] = mod;
        }

        var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IForgeClient>(forge));
        await world.SeedInstanceAsync("lazy-tabs");
        return (world, forge, world.Get<ForgePageViewModel>());
    }

    [Fact]
    public async Task OpeningTheBrowserRunsAnInitialSearch()
    {
        await using var world = await UiWorld.CreateAsync(configure: services =>
            services.AddSingleton<IForgeClient>(new FakeForgeClient().WithMod("some.mod", "1.0.0", 7))
        );
        var page = world.Get<ForgePageViewModel>();

        await page.LoadAsync(CancellationToken.None);

        page.Results.Should().NotBeEmpty("the window opens with a list, not with an empty page");
        page.HasResults.Should().BeTrue();
    }

    [Fact]
    public async Task SelectingAModFetchesOnlyTheDetailsTab()
    {
        var (world, forge, page) = await WorldAsync(Mod(1, "one.mod"));
        await using var _ = world;
        forge.DependencyRequests.Clear();
        forge.VersionRequests.Clear();

        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["one.mod"]));
        await page.DetailsLoading;

        forge.VersionRequests.Should().ContainSingle().Which.Should().Be(1, "the details tab needs the versions");
        forge.DependencyRequests.Should().BeEmpty("nobody opened the dependencies tab");
    }

    [Fact]
    public async Task OpeningTheDependenciesTabFetchesItOnceAndOnlyThen()
    {
        var (world, forge, page) = await WorldAsync(Mod(1, "one.mod"));
        await using var _ = world;
        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["one.mod"]));
        await page.DetailsLoading;
        forge.DependencyRequests.Clear();

        page.ShowTabCommand.Execute(ForgeDetailTab.Dependencies);
        await page.TabsLoading;

        forge.DependencyRequests.Should().ContainSingle();

        page.ShowTabCommand.Execute(ForgeDetailTab.Details);
        page.ShowTabCommand.Execute(ForgeDetailTab.Dependencies);
        await page.TabsLoading;

        forge.DependencyRequests.Should().ContainSingle("the tab is loaded once per mod");
    }

    [Fact]
    public async Task OpeningTheDependentsTabDoesNotFetchDependencies()
    {
        var (world, forge, page) = await WorldAsync(Mod(1, "one.mod"));
        await using var _ = world;
        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["one.mod"]));
        await page.DetailsLoading;
        forge.DependencyRequests.Clear();

        page.ShowTabCommand.Execute(ForgeDetailTab.Dependents);
        await page.TabsLoading;

        page.IsDependentsTab.Should().BeTrue();
        forge.DependencyRequests.Should().BeEmpty("nothing is installed, so there are no dependents to look for");
    }
}
