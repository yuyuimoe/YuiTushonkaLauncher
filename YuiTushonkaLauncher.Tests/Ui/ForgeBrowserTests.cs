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
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Tests.Forge;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The browser's contract with the instance: only versions the instance's SPT release accepts are offered, and the
/// sidebar is navigation, so going to a dependency and coming back works.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class ForgeBrowserTests
{
    private static ForgeMod Mod(int id, string guid, params (string Version, string Constraint)[] versions)
    {
        return new ForgeMod
        {
            Id = id,
            Guid = guid,
            Name = "Mod " + id,
            Slug = $"mod-{id}",
            Description = "<p>Hello <b>world</b></p>",
            Versions =
            [
                .. versions.Select(
                    (version, index) =>
                        new ForgeModVersion
                        {
                            Id = id * 100 + index,
                            Version = SemanticVersion.Parse(version.Version),
                            SptConstraint = VersionRange.Parse(version.Constraint),
                            DownloadUrl = $"https://sp-mod.test/{id}/{version.Version}.zip",
                        }
                ),
            ],
        };
    }

    private static async Task<ForgePageViewModel> PageAsync(UiWorld world, FakeForgeClient forge)
    {
        var page = world.Get<ForgePageViewModel>();
        await page.LoadAsync(CancellationToken.None);
        return page;
    }

    private static async Task<UiWorld> WorldAsync(FakeForgeClient forge, FakeForgePageReader pages)
    {
        return await UiWorld.CreateAsync(configure: services =>
        {
            services.AddSingleton<IForgeClient>(forge);
            services.AddSingleton<IForgePageReader>(pages);
            services.AddSingleton<IThumbnailCache>(new NoThumbnails());
        });
    }

    [Fact]
    public async Task OnlyTheVersionsTheInstanceAcceptsAreOffered()
    {
        var forge = new FakeForgeClient();
        forge.Mods["some.mod"] = Mod(1, "some.mod", ("2.0.0", "~3.11"), ("1.5.0", "~4.1"));
        await using var world = await WorldAsync(forge, new FakeForgePageReader());
        await world.SeedInstanceAsync("forge-versions");
        var page = await PageAsync(world, forge);

        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["some.mod"]));
        await page.DetailsLoading;

        page.Versions.Should().ContainSingle();
        page.Versions[0].VersionText.Should().Be("1.5.0");
        page.CanInstall.Should().BeTrue();
        page.InstallBlocker.Should().BeEmpty();
    }

    [Fact]
    public async Task AVersionTheInstanceCannotUseIsRefusedWithTheReason()
    {
        var forge = new FakeForgeClient();
        forge.Mods["old.mod"] = Mod(2, "old.mod", ("1.0.0", "~3.11"));
        await using var world = await WorldAsync(forge, new FakeForgePageReader());
        await world.SeedInstanceAsync("forge-old");
        var page = await PageAsync(world, forge);

        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["old.mod"]));
        await page.DetailsLoading;

        page.Versions.Should().BeEmpty();
        page.CanInstall.Should().BeFalse();
        page.InstallBlocker.Should().Contain("SPT").And.Contain("4.1.6");
    }

    [Fact]
    public async Task SelectingAModFillsTheDescriptionLicenceAndLinks()
    {
        var forge = new FakeForgeClient();
        forge.Mods["rich.mod"] = Mod(3, "rich.mod", ("1.0.0", "~4.1"));
        var pages = new FakeForgePageReader
        {
            Info = new ForgeModPageInfo
            {
                License = "MIT License",
                LicenseUrl = "https://choosealicense.com/licenses/mit/",
                SourceCodeUrls = ["https://github.com/example/rich"],
                VirusTotalUrls = ["https://www.virustotal.com/gui/file/abc"],
            },
        };
        await using var world = await WorldAsync(forge, pages);
        await world.SeedInstanceAsync("forge-details");
        var page = await PageAsync(world, forge);

        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["rich.mod"]));
        await page.DetailsLoading;

        page.Description.Should().Be("Hello world");
        page.LicenseText.Should().Contain("MIT License");
        page.SourceLinks.Should().ContainSingle().Which.Url.Should().Be("https://github.com/example/rich");
        page.ScanLinks.Should().ContainSingle();
        page.HasLicense.Should().BeTrue();
        page.HasSourceLinks.Should().BeTrue();
    }

    [Fact]
    public async Task GoingToADependencyAndBackKeepsTheHistory()
    {
        var forge = new FakeForgeClient();
        forge.Mods["parent.mod"] = Mod(4, "parent.mod", ("1.0.0", "~4.1"));
        forge.Mods["child.mod"] = Mod(5, "child.mod", ("1.0.0", "~4.1"));
        forge.Dependencies["parent.mod:1.0.0"] =
        [
            new ForgeDependency
            {
                Guid = "child.mod",
                Name = "Mod 5",
                ModId = 5,
                Version = SemanticVersion.Parse("1.0.0"),
            },
        ];
        await using var world = await WorldAsync(forge, new FakeForgePageReader());
        await world.SeedInstanceAsync("forge-nav");
        var page = await PageAsync(world, forge);

        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["parent.mod"]));
        await page.DetailsLoading;

        // Dependencies are a tab: they arrive when the tab is opened, not with the selection.
        page.ShowTabCommand.Execute(ForgeDetailTab.Dependencies);
        await page.TabsLoading;

        page.Dependencies.Should().ContainSingle();
        page.Dependencies[0].ModId.Should().Be(5);

        page.NavigateCommand.Execute(page.Dependencies[0]);
        await page.DetailsLoading;
        page.Shown!.Id.Should().Be(5);
        page.CanGoBack.Should().BeTrue();

        page.GoBackCommand.Execute(null);
        await page.DetailsLoading;

        page.Shown!.Id.Should().Be(4);
        page.CanGoForward.Should().BeTrue();
        page.GoForwardCommand.Execute(null);
        await page.DetailsLoading;
        page.Shown!.Id.Should().Be(5);
    }

    [Fact]
    public async Task TheTabsSwitchAndTheSidebarIsEmptyWithoutASelection()
    {
        var forge = new FakeForgeClient();
        await using var world = await WorldAsync(forge, new FakeForgePageReader());
        var page = await PageAsync(world, forge);

        page.HasSelection.Should().BeFalse();
        page.IsDescriptionTab.Should().BeTrue();

        page.ShowTabCommand.Execute(ForgeDetailTab.Dependents);

        page.IsDependentsTab.Should().BeTrue();
        page.IsDescriptionTab.Should().BeFalse();
    }

    /// <summary>No thumbnails: the row keeps its letter tile, which is what the view falls back to.</summary>
    private sealed class NoThumbnails : IThumbnailCache
    {
        public Task<string?> GetAsync(string? url, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>A page reader that answers with what the site would say, or with nothing.</summary>
    private sealed class FakeForgePageReader : IForgePageReader
    {
        public ForgeModPageInfo? Info { get; set; }

        public Task<ForgeModPageInfo?> ReadAsync(int modId, string slug, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Info);
        }

        public ForgeModPageInfo? Parse(string html)
        {
            return Info;
        }
    }
}
