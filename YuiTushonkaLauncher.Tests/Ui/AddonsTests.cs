using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Tests.Forge;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// Addons are not mods: they hang off a parent that must already be installed, the page groups them by that parent,
/// and the browser refuses an addon whose parent is missing or whose version it does not fit.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class AddonsTests
{
    private static async Task InstallParentRowAsync(UiWorld world, Guid instanceId, string guid, string name, string version)
    {
        var factory = world.Get<LauncherDbContextFactory>();
        await using var context = factory.CreateDbContext();
        context.InstalledMods.Add(
            new InstalledMod
            {
                Id = Guid.NewGuid(),
                InstanceId = instanceId,
                Guid = guid,
                Name = name,
                Version = SemanticVersion.Parse(version),
                Kind = ModKind.Client,
            }
        );
        await context.SaveChangesAsync();
    }

    private static async Task AddAddonRowAsync(UiWorld world, Guid instanceId, string guid, string name, string parentGuid, string version)
    {
        var factory = world.Get<LauncherDbContextFactory>();
        await using var context = factory.CreateDbContext();
        context.InstalledMods.Add(
            new InstalledMod
            {
                Id = Guid.NewGuid(),
                InstanceId = instanceId,
                Guid = guid,
                Name = name,
                Version = SemanticVersion.Parse(version),
                Kind = ModKind.Client,
                ParentModGuid = parentGuid,
            }
        );
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task TheAddonsPageGroupsInstalledAddonsUnderTheirParent()
    {
        await using var world = await UiWorld.CreateAsync();
        var instance = await world.SeedInstanceAsync("addons-page");
        await InstallParentRowAsync(world, instance.Id, "parent.mod", "Parent Mod", "1.2.3");
        await AddAddonRowAsync(world, instance.Id, "addon.one", "Addon One", "parent.mod", "0.1.0");
        await AddAddonRowAsync(world, instance.Id, "addon.two", "Addon Two", "parent.mod", "0.2.0");

        var page = world.Get<AddonsPageViewModel>();
        await page.LoadAsync(CancellationToken.None);

        page.HasAddons.Should().BeTrue();
        page.Groups.Should().ContainSingle();
        page.Groups[0].ParentName.Should().Be("Parent Mod");
        page.Groups[0].HeaderText.Should().Contain("1.2.3");
        page.Groups[0].Addons.Select(row => row.Name).Should().BeEquivalentTo(["Addon One", "Addon Two"]);
    }

    [Fact]
    public async Task TheAddonsPageIsEmptyWithoutInstalledAddons()
    {
        await using var world = await UiWorld.CreateAsync();
        await world.SeedInstanceAsync("addons-empty");

        var page = world.Get<AddonsPageViewModel>();
        await page.LoadAsync(CancellationToken.None);

        page.HasAddons.Should().BeFalse();
        page.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task InstallAddonsOpensTheBrowserInAddonMode()
    {
        var windows = new FakeWindowController();
        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IWindowController>(windows));
        await world.SeedInstanceAsync("addons-open");
        var page = world.Get<AddonsPageViewModel>();
        await page.LoadAsync(CancellationToken.None);

        page.InstallAddonsCommand.Execute(null);

        windows.ForgeModes.Should().ContainSingle().Which.Should().Be(ForgeBrowserMode.Addons);
    }

    [Fact]
    public async Task TheModsPageOpensTheBrowserInModMode()
    {
        var windows = new FakeWindowController();
        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IWindowController>(windows));
        await world.SeedInstanceAsync("mods-browse");
        var page = world.Get<ModsPageViewModel>();
        await page.LoadAsync(CancellationToken.None);

        page.OpenBrowserCommand.Execute(null);

        windows.ForgeModes.Should().ContainSingle().Which.Should().Be(ForgeBrowserMode.Mods);
    }

    [Fact]
    public async Task AnAddonWhoseParentIsMissingCannotBeInstalled()
    {
        var forge = new FakeForgeClient();
        forge.Mods["parent.mod"] = new ForgeMod
        {
            Id = 40,
            Guid = "parent.mod",
            Name = "Parent Mod",
            Slug = "parent-mod",
        };
        forge.Addons[40] =
        [
            new ForgeAddon
            {
                Id = 400,
                ModId = 40,
                Name = "Addon One",
                Slug = "addon-one",
            },
        ];
        forge.AddonVersions[400] =
        [
            new ForgeAddonVersion
            {
                Id = 1,
                Version = SemanticVersion.Parse("1.0.0"),
                ModVersionConstraint = VersionRange.Parse("~1.2"),
                DownloadUrl = "https://sp-mod.test/addon.zip",
            },
        ];

        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IForgeClient>(forge));
        await world.SeedInstanceAsync("addon-missing");
        var page = world.Get<ForgePageViewModel>();
        page.Mode = ForgeBrowserMode.Addons;

        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["parent.mod"]));
        await page.DetailsLoading;
        await page.UseAsParentCommand.ExecuteAsync(page.Shown);

        page.IsRequirementMet.Should().BeFalse();
        page.RequirementText.Should().Contain("not installed");
        page.AddonResults.Should().ContainSingle().Which.CanInstall.Should().BeFalse();
    }

    [Fact]
    public async Task InstallingAMissingParentAddonAsksForNothing()
    {
        var forge = new FakeForgeClient();
        forge.Mods["parent.mod"] = new ForgeMod
        {
            Id = 40,
            Guid = "parent.mod",
            Name = "Parent Mod",
            Slug = "parent-mod",
        };
        forge.Addons[40] =
        [
            new ForgeAddon
            {
                Id = 400,
                ModId = 40,
                Name = "Addon One",
                Slug = "addon-one",
            },
        ];
        forge.AddonVersions[400] =
        [
            new ForgeAddonVersion
            {
                Id = 1,
                Version = SemanticVersion.Parse("1.0.0"),
                ModVersionConstraint = VersionRange.Parse("~1.2"),
                DownloadUrl = "https://sp-mod.test/addon.zip",
            },
        ];

        var addons = new RecordingAddonService();
        await using var world = await UiWorld.CreateAsync(configure: services =>
        {
            services.AddSingleton<IForgeClient>(forge);
            services.AddSingleton<IAddonService>(addons);
        });
        await world.SeedInstanceAsync("addon-refused");
        var page = world.Get<ForgePageViewModel>();
        page.Mode = ForgeBrowserMode.Addons;
        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["parent.mod"]));
        await page.DetailsLoading;
        await page.UseAsParentCommand.ExecuteAsync(page.Shown);

        await page.InstallAddonCommand.ExecuteAsync(page.AddonResults[0]);

        addons.Requests.Should().BeEmpty("the parent is missing, so nothing may be installed");
        page.Error.Should().Contain("not installed");
    }

    /// <summary>Records what it was asked to install; the real service owns the constraint check.</summary>
    private sealed class RecordingAddonService : IAddonService
    {
        public List<AddonInstallRequest> Requests { get; } = [];

        public Task<bool> CanInstallAsync(
            Guid instanceId,
            string parentModGuid,
            VersionRange constraint,
            SemanticVersion parentVersion,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(true);
        }

        public Task<AddonInstallResult> InstallAsync(AddonInstallRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            throw new InvalidOperationException("the test only asserts that the call never happens");
        }

        public Task UninstallAsync(Guid instanceId, string addonGuid, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WithTheParentInstalledTheAddonCanBeInstalled()
    {
        var forge = new FakeForgeClient();
        forge.Mods["parent.mod"] = new ForgeMod
        {
            Id = 40,
            Guid = "parent.mod",
            Name = "Parent Mod",
            Slug = "parent-mod",
        };
        forge.Addons[40] =
        [
            new ForgeAddon
            {
                Id = 400,
                ModId = 40,
                Name = "Addon One",
                Slug = "addon-one",
            },
        ];
        forge.AddonVersions[400] =
        [
            new ForgeAddonVersion
            {
                Id = 1,
                Version = SemanticVersion.Parse("1.0.0"),
                ModVersionConstraint = VersionRange.Parse("~1.2"),
                DownloadUrl = "https://sp-mod.test/addon.zip",
            },
        ];

        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IForgeClient>(forge));
        var instance = await world.SeedInstanceAsync("addon-present");
        await InstallParentRowAsync(world, instance.Id, "parent.mod", "Parent Mod", "1.2.3");
        await world.Get<YuiTushonkaLauncher.Navigation.IInstanceContext>().RefreshAsync();

        var page = world.Get<ForgePageViewModel>();
        page.Mode = ForgeBrowserMode.Addons;
        page.ShowDetailsCommand.Execute(new ForgeModRowViewModel(world.Localizer, forge.Mods["parent.mod"]));
        await page.DetailsLoading;
        await page.UseAsParentCommand.ExecuteAsync(page.Shown);

        page.IsRequirementMet.Should().BeTrue();
        page.AddonResults.Should().ContainSingle().Which.CanInstall.Should().BeTrue();
    }
}
