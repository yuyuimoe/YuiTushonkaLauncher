using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Infrastructure.Mods;
using YuiTushonkaLauncher.Tests.Forge;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Instances;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class DependencyResolverTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private TempDirectory _locations = null!;
    private FakeBaseInstall _install = null!;
    private Instance _instance = null!;
    private FakeForgeClient _forge = null!;
    private DependencyResolver _resolver = null!;

    public async Task InitializeAsync()
    {
        _database = new TestDatabase();
        await _database.InitializeAsync();
        _locations = new TempDirectory();
        _install = FakeBaseInstall.CreateSpt41();
        _forge = new FakeForgeClient();

        var instances = new InstanceService(
            _database.UnitOfWork,
            new FileLinkService(NullLogger<FileLinkService>.Instance),
            FakeBaseInstall.CreateProbe(),
            new PatchStateDetector(NullLogger<PatchStateDetector>.Instance),
            NullLogger<InstanceService>.Instance
        );

        _instance = await instances.CreateAsync(
            new InstanceCreationRequest
            {
                Name = "deps",
                Location = _locations.Path,
                BaseInstallPath = _install.Root,
            }
        );

        _resolver = new DependencyResolver(_database.UnitOfWork, _forge, NullLogger<DependencyResolver>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        _locations.Dispose();
        _install.Dispose();
    }

    [Fact]
    public async Task ResolveAsync_OrdersDependenciesBeforeDependents()
    {
        _forge.WithMod("com.a.top", "1.0.0").WithMod("com.b.middle", "2.0.0").WithMod("com.c.bottom", "3.0.0");
        _forge.WithDependency("com.a.top", "1.0.0", "com.b.middle", "2.0.0");
        _forge.WithDependency("com.b.middle", "2.0.0", "com.c.bottom", "3.0.0");

        var plan = await _resolver.ResolveAsync(_instance.Id, [Request("com.a.top")]);

        plan.IsResolvable.Should().BeTrue();
        plan.Install.Select(package => package.Guid).Should().Equal("com.c.bottom", "com.b.middle", "com.a.top");
        plan.Install.Single(package => package.Guid == "com.b.middle").RequiredBy.Should().Be("com.a.top");
        plan.Install.Select(package => package.Order).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ResolveAsync_SkipsOptionalDependencies()
    {
        _forge.WithMod("com.a.top", "1.0.0").WithMod("com.optional", "1.0.0");
        _forge.WithDependency("com.a.top", "1.0.0", "com.optional", optional: true);

        var plan = await _resolver.ResolveAsync(_instance.Id, [Request("com.a.top")]);

        plan.Install.Should().ContainSingle().Which.Guid.Should().Be("com.a.top");
    }

    [Fact]
    public async Task ResolveAsync_ReportsAnUnknownGuid()
    {
        var plan = await _resolver.ResolveAsync(_instance.Id, [Request("com.does.not.exist")]);

        plan.IsResolvable.Should().BeFalse();
        plan.Problems.Should().ContainSingle().Which.Guid.Should().Be("com.does.not.exist");
    }

    [Fact]
    public async Task ResolveAsync_DetectsCycles()
    {
        _forge.WithMod("com.a", "1.0.0").WithMod("com.b", "1.0.0");
        _forge.WithDependency("com.a", "1.0.0", "com.b");
        _forge.WithDependency("com.b", "1.0.0", "com.a");

        var plan = await _resolver.ResolveAsync(_instance.Id, [Request("com.a")]);

        plan.Cycles.Should().NotBeEmpty();
        plan.IsResolvable.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_FlagsAlreadyInstalledPackages()
    {
        _forge.WithMod("com.installed", "1.0.0");

        await using (var context = _database.CreateContext())
        {
            context.InstalledMods.Add(
                new InstalledMod
                {
                    Id = Guid.NewGuid(),
                    InstanceId = _instance.Id,
                    Guid = "com.installed",
                    Name = "Installed",
                    Version = SemanticVersion.Parse("1.0.0"),
                    Kind = ModKind.Client,
                    Recognition = ModRecognition.Recognized,
                    InstalledAt = DateTimeOffset.UtcNow,
                }
            );

            await context.SaveChangesAsync();
        }

        var plan = await _resolver.ResolveAsync(_instance.Id, [Request("com.installed")]);

        plan.Install.Should().ContainSingle().Which.AlreadyInstalled.Should().BeTrue();
        plan.Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_BatchesTheCatalogueLookup()
    {
        _forge.WithMod("com.a", "1.0.0").WithMod("com.b", "1.0.0").WithMod("com.c", "1.0.0");

        await _resolver.ResolveAsync(_instance.Id, [Request("com.a"), Request("com.b"), Request("com.c")]);

        _forge.GuidRequests.Should().ContainSingle("one batched lookup is enough for a single level");
        _forge.GuidRequests[0].Should().HaveCount(3);
    }

    [Fact]
    public async Task ResolveAsync_WithoutRequests_IsEmpty()
    {
        var plan = await _resolver.ResolveAsync(_instance.Id, []);

        plan.Install.Should().BeEmpty();
        plan.IsResolvable.Should().BeTrue();
    }

    private static PackageRequest Request(string guid)
    {
        return new PackageRequest { Guid = guid };
    }
}
