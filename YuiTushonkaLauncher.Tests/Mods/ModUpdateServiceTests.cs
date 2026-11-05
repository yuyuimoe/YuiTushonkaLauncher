using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Mods;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class ModUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_OffersACompatibleUpdate()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();

        context.Forge.Report = new ForgeUpdateReport
        {
            SptVersion = context.Instance.SptVersion,
            Updates =
            [
                new ForgeUpdate
                {
                    Current = new ForgeUpdateTarget
                    {
                        Guid = "com.fixture.clientmod",
                        Name = "Fixture",
                        Version = SemanticVersion.Parse("1.4.2"),
                    },
                    Recommended = new ForgeUpdateCandidate
                    {
                        VersionId = 2,
                        Version = SemanticVersion.Parse("1.5.0"),
                        DownloadUrl = "https://sp-mod.com/mod/download/9/x/1.5.0",
                        ContentLength = 1024,
                    },
                },
            ],
        };

        var plan = await context.Updates.CheckAsync(context.Instance.Id);

        plan.IsClean.Should().BeFalse();
        var status = plan.Statuses.Single();
        status.Decision.Should().Be(UpdateDecision.Update);
        status.AvailableVersion.Should().Be(SemanticVersion.Parse("1.5.0"));
        status.DownloadUrl.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_RespectsPinsUnlessAsked()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        await context.Installer.SetPinnedAsync(context.Instance.Id, "com.fixture.clientmod", pinned: true);

        context.Forge.Report = new ForgeUpdateReport
        {
            SptVersion = context.Instance.SptVersion,
            Updates =
            [
                new ForgeUpdate
                {
                    Current = new ForgeUpdateTarget
                    {
                        Guid = "com.fixture.clientmod",
                        Name = "Fixture",
                        Version = SemanticVersion.Parse("1.4.2"),
                    },
                    Recommended = new ForgeUpdateCandidate
                    {
                        VersionId = 2,
                        Version = SemanticVersion.Parse("1.5.0"),
                        DownloadUrl = "https://sp-mod.com/x",
                    },
                },
            ],
        };

        var pinned = await context.Updates.CheckAsync(context.Instance.Id);
        var forced = await context.Updates.CheckAsync(context.Instance.Id, includePinned: true);

        pinned.Statuses.Single().Decision.Should().Be(UpdateDecision.Pinned);
        pinned.IsClean.Should().BeTrue();
        forced.Statuses.Single().Decision.Should().Be(UpdateDecision.Update);
    }

    [Fact]
    public async Task CheckAsync_ReportsIncompatibleMods()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();

        context.Forge.Report = new ForgeUpdateReport
        {
            SptVersion = context.Instance.SptVersion,
            Incompatible =
            [
                new ForgeIncompatible
                {
                    Current = new ForgeUpdateTarget
                    {
                        Guid = "com.fixture.clientmod",
                        Name = "Fixture",
                        Version = SemanticVersion.Parse("1.4.2"),
                    },
                    Reason = "no_version_for_spt",
                },
            ],
        };

        var plan = await context.Updates.CheckAsync(context.Instance.Id);

        var status = plan.Statuses.Single();
        status.Decision.Should().Be(UpdateDecision.Incompatible);
        status.Reason.Should().Contain("no_version_for_spt");
        plan.IsClean.Should().BeTrue("an incompatible mod is not an available update");
    }

    [Fact]
    public async Task CheckAsync_WithNoMods_IsClean()
    {
        await using var context = await ModTestContext.CreateAsync();

        var plan = await context.Updates.CheckAsync(context.Instance.Id);

        plan.Statuses.Should().BeEmpty();
        plan.IsClean.Should().BeTrue();
        plan.SptVersion.Should().Be(context.Instance.SptVersion);
    }

    [Fact]
    public async Task CheckAsync_UnknownInstance_IsRejected()
    {
        await using var context = await ModTestContext.CreateAsync();

        var act = async () => await context.Updates.CheckAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<Core.Instances.InstanceValidationException>();
    }
}
