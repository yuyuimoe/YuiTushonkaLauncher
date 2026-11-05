using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// Clicking the dependents tab twice, or navigating away while the catalogue is being read, must not walk the catalogue
/// again: the old code tied the walk to the page's cancellation token and restarted it on every ask, which is what a
/// hundred requests per click looks like. These tests pin the walk to the graph instead.
/// </summary>
public sealed class DependencyGraphStormTests
{
    [Fact]
    public async Task TwoAsksShareOneWalkOfTheCatalogue()
    {
        using var temp = new TempDirectory();
        var forge = new RecordingForge();
        var graph = new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance);

        var first = graph.DependentsOfAsync(902, cancellationToken: CancellationToken.None);
        var second = graph.DependentsOfAsync(902, cancellationToken: CancellationToken.None);
        await Task.WhenAll(first, second);

        forge.CatalogueCalls.Should().Be(1, "one walk, shared by everyone who asked");
        forge.DependencyCalls.Should().Be(1);
    }

    [Fact]
    public async Task AWarmUpAndALaterAskStillWalkTheCatalogueOnce()
    {
        using var temp = new TempDirectory();
        var forge = new RecordingForge();
        var graph = new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance);

        graph.WarmUp();
        var dependents = await graph.DependentsOfAsync(902, cancellationToken: CancellationToken.None);

        dependents.Should().ContainSingle();
        forge.CatalogueCalls.Should().Be(1);
    }

    [Fact]
    public async Task NavigatingAwayDoesNotCancelTheWalk()
    {
        using var temp = new TempDirectory();
        var forge = new RecordingForge { HoldFirstPage = true };
        var graph = new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance);

        using var cancellation = new CancellationTokenSource();
        var waiting = graph.DependentsOfAsync(902, cancellationToken: cancellation.Token);
        await forge.FirstPageReached.Task;
        await cancellation.CancelAsync();

        await waiting.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();

        forge.Release();
        var dependents = await graph.DependentsOfAsync(902, cancellationToken: CancellationToken.None);

        dependents.Should().NotBeEmpty("the walk carried on without the page that started it");
        forge.CatalogueCalls.Should().Be(1, "the next ask found the walk already done, not a reason to start another");
    }

    [Fact]
    public async Task AFailedWalkIsNotRetriedOnEveryClick()
    {
        using var temp = new TempDirectory();
        var forge = new RecordingForge { Fail = true };
        var graph = new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance);

        await graph.Invoking(g => g.DependentsOfAsync(902)).Should().ThrowAsync<ForgeException>();
        var callsAfterFirst = forge.CatalogueCalls;

        await graph.Invoking(g => g.DependentsOfAsync(902)).Should().ThrowAsync<ForgeException>();
        await graph.Invoking(g => g.DependentsOfAsync(902)).Should().ThrowAsync<ForgeException>();

        forge
            .CatalogueCalls.Should()
            .Be(callsAfterFirst, "a failed walk waits out its cooldown instead of answering every click with requests");
    }

    [Fact]
    public async Task ACatalogueThatIgnoresThePageNumberIsNotAskedForever()
    {
        using var temp = new TempDirectory();
        var forge = new RecordingForge { RepeatSamePage = true };
        var graph = new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance);

        await graph.DependentsOfAsync(902);

        // Page 1 arrives, page 2 is page 1 again: the walk stops rather than asking for twenty more of the same.
        forge.CatalogueCalls.Should().BeLessThanOrEqualTo(2);
    }

    /// <summary>A catalogue of one page, with switches for the shapes that used to produce request storms.</summary>
    private sealed class RecordingForge : IForgeClient
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CatalogueCalls { get; private set; }

        public int DependencyCalls { get; private set; }

        public bool Fail { get; init; }

        public bool RepeatSamePage { get; init; }

        public bool HoldFirstPage { get; init; }

        public TaskCompletionSource FirstPageReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release()
        {
            _release.TrySetResult();
        }

        public async Task<ForgePage<ForgeMod>> GetModsAsync(ForgeModQuery query, CancellationToken cancellationToken = default)
        {
            CatalogueCalls++;
            if (Fail)
            {
                throw new ForgeException("the catalogue is unreachable");
            }

            if (HoldFirstPage && CatalogueCalls == 1)
            {
                FirstPageReached.TrySetResult();
                await _release.Task;
            }

            var mods = new List<ForgeMod> { Mod(791, "me.sol.sain"), Mod(902, "xyz.drakia.bigbrain") };

            if (RepeatSamePage && query.Page > 1)
            {
                // Same first id as page 1, which is how a server that ignores `page` looks.
                return new ForgePage<ForgeMod>
                {
                    Items = mods,
                    Page = query.Page,
                    PerPage = 50,
                    Total = 10_000,
                    LastPage = 200,
                };
            }

            return new ForgePage<ForgeMod>
            {
                Items = mods,
                Page = query.Page,
                PerPage = 50,
                Total = mods.Count,
                LastPage = 1,
            };
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>> GetDependenciesAsync(
            IReadOnlyCollection<InstalledPackage> packages,
            SemanticVersion sptVersion,
            CancellationToken cancellationToken = default
        )
        {
            DependencyCalls++;
            var answers = new Dictionary<string, IReadOnlyList<ForgeDependency>>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                answers[$"{package.Guid}:{package.Version}"] =
                    package.Guid == "me.sol.sain"
                        ?
                        [
                            new ForgeDependency
                            {
                                ModId = 902,
                                Guid = "xyz.drakia.bigbrain",
                                Name = "BigBrain",
                                Version = SemanticVersion.Parse("1.5.0"),
                            },
                        ]
                        : [];
            }

            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>>(answers);
        }

        public Task<ForgePing> PingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ForgePing { Message = "ok" });
        }

        public Task<ForgeMod> GetModAsync(int modId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The graph never asks for one mod.");
        }

        public Task<IReadOnlyList<ForgeModVersion>> GetModVersionsAsync(int modId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The graph reads versions with the catalogue page.");
        }

        public Task<IReadOnlyList<ForgeMod>> FindModsByGuidsAsync(
            IReadOnlyCollection<string> guids,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException("The graph never searches by GUID.");
        }

        public Task<ForgeUpdateReport> CheckUpdatesAsync(
            IReadOnlyCollection<InstalledPackage> installed,
            SemanticVersion sptVersion,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException("The graph never checks for updates.");
        }

        public Task<ForgePage<ForgeAddon>> GetAddonsAsync(ForgeAddonQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The graph never reads addons.");
        }

        public Task<IReadOnlyList<ForgeAddonVersion>> GetAddonVersionsAsync(int addonId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The graph never reads addon versions.");
        }

        public Task<IReadOnlyList<ForgeSptVersion>> GetSptVersionsAsync(int perPage = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ForgeSptVersion>>([
                new ForgeSptVersion { ForgeId = 1, Version = SemanticVersion.Parse("4.1.6") },
            ]);
        }

        public Task<IReadOnlyList<ForgeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ForgeCategory>>([]);
        }

        private static ForgeMod Mod(int id, string guid)
        {
            return new ForgeMod
            {
                Id = id,
                Guid = guid,
                Name = guid,
                Slug = guid,
                Teaser = string.Empty,
                Versions =
                [
                    new ForgeModVersion
                    {
                        Id = id * 10,
                        Version = SemanticVersion.Parse("1.0.0"),
                        SptConstraint = VersionRange.Parse("~4.1"),
                        DownloadUrl = $"https://sp-mod.test/{id}.zip",
                    },
                ],
            };
        }
    }
}
