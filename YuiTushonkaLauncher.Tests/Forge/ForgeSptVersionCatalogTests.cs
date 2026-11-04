using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeSptVersionCatalogTests
{
    [Fact]
    public async Task SyncAsync_UpsertsReleasesAndFlagsExactlyOneAsCurrent()
    {
        await using var database = new Infrastructure.TestDatabase();
        await database.InitializeAsync();

        var fake = new FakeForgeClient
        {
            Releases =
            [
                new ForgeSptVersion
                {
                    ForgeId = 55,
                    Version = SemanticVersion.Parse("4.1.6"),
                    ModCount = 738,
                },
                new ForgeSptVersion
                {
                    ForgeId = 40,
                    Version = SemanticVersion.Parse("4.0.10"),
                    ModCount = 700,
                },
                new ForgeSptVersion
                {
                    ForgeId = 2,
                    Version = SemanticVersion.Parse("3.11.4"),
                    ModCount = 667,
                },
            ],
        };

        var catalog = new ForgeSptVersionCatalog(fake, database.UnitOfWork, NullLogger<ForgeSptVersionCatalog>.Instance);

        var first = await catalog.SyncAsync();
        var second = await catalog.SyncAsync();

        first.Should().HaveCount(3);
        second.Should().HaveCount(3, "a second sync must update, not duplicate");
        second.Count(release => release.IsCurrent).Should().Be(1);
        second.Single(release => release.IsCurrent).Version.Should().Be(SemanticVersion.Parse("4.1.6"));
    }

    [Fact]
    public async Task SyncAsync_KeepsTheCacheWhenTheApiReturnsNothing()
    {
        await using var database = new Infrastructure.TestDatabase();
        await database.InitializeAsync();

        var fake = new FakeForgeClient { Releases = [new ForgeSptVersion { ForgeId = 55, Version = SemanticVersion.Parse("4.1.6") }] };
        var catalog = new ForgeSptVersionCatalog(fake, database.UnitOfWork, NullLogger<ForgeSptVersionCatalog>.Instance);
        await catalog.SyncAsync();

        fake.Releases = [];
        var afterEmptyResponse = await catalog.SyncAsync();

        afterEmptyResponse.Should().ContainSingle();
        afterEmptyResponse[0].Version.Should().Be(SemanticVersion.Parse("4.1.6"));
    }

    private sealed class FakeForgeClient : IForgeClient
    {
        public IReadOnlyList<ForgeSptVersion> Releases { get; set; } = [];

        public Task<IReadOnlyList<ForgeSptVersion>> GetSptVersionsAsync(int perPage = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Releases);
        }

        public Task<ForgePing> PingAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<ForgePage<ForgeMod>> GetModsAsync(ForgeModQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<ForgeMod> GetModAsync(int modId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<ForgeModVersion>> GetModVersionsAsync(int modId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<ForgeMod>> FindModsByGuidsAsync(
            IReadOnlyCollection<string> guids,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }

        public Task<ForgeUpdateReport> CheckUpdatesAsync(
            IReadOnlyCollection<InstalledPackage> installed,
            SemanticVersion sptVersion,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>> GetDependenciesAsync(
            IReadOnlyCollection<InstalledPackage> packages,
            SemanticVersion sptVersion,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }

        public Task<ForgePage<ForgeAddon>> GetAddonsAsync(ForgeAddonQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<ForgeAddonVersion>> GetAddonVersionsAsync(int addonId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<ForgeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
