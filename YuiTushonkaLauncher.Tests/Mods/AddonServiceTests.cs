using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class AddonServiceTests
{
    [Fact]
    public async Task InstallAsync_PlacesTheAddonNextToItsParent()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();

        using var addon = new ZipFixture().Add("Languages/pt-BR.json", "{}");

        var result = await context.Addons.InstallAsync(
            new AddonInstallRequest
            {
                InstanceId = context.Instance.Id,
                ParentModGuid = "com.fixture.clientmod",
                ArchivePath = addon.Path,
                Version = SemanticVersion.Parse("1.0.0"),
                ModVersionConstraint = Core.Domain.VersionRange.Parse("^1.4.0"),
            }
        );

        result.Files.Should().Contain(path => path.StartsWith("BepInEx/plugins/fixture/"));
        File.Exists(Path.Combine(context.Instance.Path, result.Files[0])).Should().BeTrue();

        await using var verification = context.Database.CreateContext();
        var row = await verification.InstalledMods.SingleAsync(mod => mod.Guid == result.Guid);
        row.ParentModGuid.Should().Be("com.fixture.clientmod");
        row.IsAddon.Should().BeTrue();
    }

    [Fact]
    public async Task InstallAsync_RejectsAModVersionConstraintMismatch()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        using var addon = new ZipFixture().Add("readme.txt", "x");

        var act = async () =>
            await context.Addons.InstallAsync(
                new AddonInstallRequest
                {
                    InstanceId = context.Instance.Id,
                    ParentModGuid = "com.fixture.clientmod",
                    ArchivePath = addon.Path,
                    Version = SemanticVersion.Parse("2.0.0"),
                    ModVersionConstraint = Core.Domain.VersionRange.Parse("^2.0.0"),
                }
            );

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_RejectsAMissingParent()
    {
        await using var context = await ModTestContext.CreateAsync();
        using var addon = new ZipFixture().Add("readme.txt", "x");

        var act = async () =>
            await context.Addons.InstallAsync(
                new AddonInstallRequest
                {
                    InstanceId = context.Instance.Id,
                    ParentModGuid = "com.not.installed",
                    ArchivePath = addon.Path,
                }
            );

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task CanInstallAsync_ChecksTheConstraintAgainstTheInstalledVersion()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();

        var compatible = await context.Addons.CanInstallAsync(
            context.Instance.Id,
            "com.fixture.clientmod",
            Core.Domain.VersionRange.Parse("~1.4.0"),
            SemanticVersion.Parse("1.4.2")
        );

        var incompatible = await context.Addons.CanInstallAsync(
            context.Instance.Id,
            "com.fixture.clientmod",
            Core.Domain.VersionRange.Parse("^2.0.0"),
            SemanticVersion.Parse("1.4.2")
        );

        compatible.Should().BeTrue();
        incompatible.Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_RemovesOnlyTheAddon()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        using var addon = new ZipFixture().Add("Languages/pt-BR.json", "{}");

        var installed = await context.Addons.InstallAsync(
            new AddonInstallRequest
            {
                InstanceId = context.Instance.Id,
                ParentModGuid = "com.fixture.clientmod",
                ArchivePath = addon.Path,
            }
        );

        await context.Addons.UninstallAsync(context.Instance.Id, installed.Guid);

        await using var verification = context.Database.CreateContext();
        (await verification.InstalledMods.CountAsync()).Should().Be(1, "the parent mod stays installed");
        File.Exists(Path.Combine(context.Instance.Path, installed.Files[0])).Should().BeFalse();
    }

    [Fact]
    public async Task InstallAsync_FromADownload_ExtractsAndRecordsTheAddon()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        using var addon = new ZipFixture().Add("Languages/pt-BR.json", "{}");
        context.DownloadSource = addon.Path;

        try
        {
            var result = await context.Addons.InstallAsync(
                new AddonInstallRequest
                {
                    InstanceId = context.Instance.Id,
                    ParentModGuid = "com.fixture.clientmod",
                    DownloadUrl = new Uri("https://sp-mod.com/mod/download/addon/1/x/1.0.0"),
                }
            );

            result.Files.Should().NotBeEmpty();
        }
        finally
        {
            context.DownloadSource = null;
        }
    }

    [Fact]
    public async Task InstallAsync_HostileAddonArchive_IsRejected()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        using var addon = new ZipFixture().Add("../../escape.json", "evil");

        var act = async () =>
            await context.Addons.InstallAsync(
                new AddonInstallRequest
                {
                    InstanceId = context.Instance.Id,
                    ParentModGuid = "com.fixture.clientmod",
                    ArchivePath = addon.Path,
                }
            );

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_MissingArchive_IsRejected()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();

        var act = async () =>
            await context.Addons.InstallAsync(
                new AddonInstallRequest
                {
                    InstanceId = context.Instance.Id,
                    ParentModGuid = "com.fixture.clientmod",
                    ArchivePath = "/nope.zip",
                }
            );

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_WithoutArchiveOrUrl_IsRejected()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();

        var act = async () =>
            await context.Addons.InstallAsync(
                new AddonInstallRequest { InstanceId = context.Instance.Id, ParentModGuid = "com.fixture.clientmod" }
            );

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_Twice_ReplacesTheAddonRow()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        using var addon = new ZipFixture().Add("Languages/pt-BR.json", "{}");
        var request = new AddonInstallRequest
        {
            InstanceId = context.Instance.Id,
            ParentModGuid = "com.fixture.clientmod",
            ArchivePath = addon.Path,
        };

        var first = await context.Addons.InstallAsync(request);
        var second = await context.Addons.InstallAsync(request);

        second.Guid.Should().Be(first.Guid);
        await using var verification = context.Database.CreateContext();
        var addons = await verification.InstalledMods.Where(mod => mod.ParentModGuid != null).ToListAsync();
        addons.Should().ContainSingle();
    }

    [Fact]
    public async Task UninstallAsync_RejectsAnUnknownAddon()
    {
        await using var context = await ModTestContext.CreateAsync();

        var act = async () => await context.Addons.UninstallAsync(context.Instance.Id, "com.not.installed");

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_FromAForgeDownload_RecordsTheForgeIdAndTheSource()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        using var addon = new ZipFixture().Add("Languages/pt-BR.json", "{}");
        context.DownloadSource = addon.Path;

        await context.Addons.InstallAsync(
            new AddonInstallRequest
            {
                InstanceId = context.Instance.Id,
                ParentModGuid = "com.fixture.clientmod",
                ForgeId = 99,
                DownloadUrl = new Uri("https://sp-mod.com/addon/download/99/x/1.0.0"),
            }
        );

        await using var verification = context.Database.CreateContext();
        var row = await verification.InstalledMods.Include(mod => mod.Mod).SingleAsync(mod => mod.ParentModGuid != null);
        row.Mod.Should().NotBeNull();
        row.Mod!.ForgeId.Should().Be(99, "an export needs the Forge id to name the addon");
        row.Source.Should().Be(InstalledModSource.Forge);
    }

    [Fact]
    public async Task InstallAsync_FromALocalArchive_HasNoCatalogueRow()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        using var addon = new ZipFixture().Add("Languages/pt-BR.json", "{}");

        await context.Addons.InstallAsync(
            new AddonInstallRequest
            {
                InstanceId = context.Instance.Id,
                ParentModGuid = "com.fixture.clientmod",
                ArchivePath = addon.Path,
            }
        );

        await using var verification = context.Database.CreateContext();
        var row = await verification.InstalledMods.SingleAsync(mod => mod.ParentModGuid != null);
        row.ModId.Should().BeNull();
        row.Source.Should().Be(InstalledModSource.LocalArchive);
    }
}
