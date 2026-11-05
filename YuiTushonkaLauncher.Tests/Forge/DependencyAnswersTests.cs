using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// <c>/mods/dependencies</c> names the mod with <c>id</c> and the installable version inside <c>latest_compatible_version</c>.
/// Reading <c>mod_id</c> instead — which the API never sends — made every dependency vanish from the sidebar, so the
/// shape is pinned here against the answer the API actually gives. The graph is then checked to turn those answers
/// around, and to keep them instead of walking the catalogue again.
/// </summary>
public sealed class DependencyAnswersTests
{
    private const string Answer = """
        {"success":true,"data":{"me.sol.sain:4.5.0":[
          {"id":902,"guid":"xyz.drakia.bigbrain","name":"BigBrain","slug":"bigbrain",
           "latest_compatible_version":{"id":14411,"version":"1.5.0","link":"https://sp-mod.com/mod/download/902/bigbrain/1.5.0"},
           "conflict":false,"dependencies":[]},
          {"id":827,"guid":"xyz.drakia.waypoints","name":"Waypoints","slug":"waypoints",
           "latest_compatible_version":{"id":14423,"version":"1.9.0","link":"https://sp-mod.com/mod/download/827/waypoints/1.9.0"},
           "conflict":true,"dependencies":[]}
        ]}}
        """;

    [Fact]
    public async Task TheDependencyIdsAndVersionsComeFromTheAnswerTheApiGives()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods/dependencies").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Answer));

        var answers = await harness.Client.GetDependenciesAsync(
            [new InstalledPackage { Guid = "me.sol.sain", Version = SemanticVersion.Parse("4.5.0") }],
            SemanticVersion.Parse("4.1.6"),
            CancellationToken.None
        );

        var dependencies = answers["me.sol.sain:4.5.0"];
        dependencies.Should().HaveCount(2);
        dependencies.Select(dependency => dependency.ModId).Should().Equal(902, 827);
        dependencies.Select(dependency => dependency.Version!.ToString()).Should().Equal("1.5.0", "1.9.0");
        dependencies[1].IsConflict.Should().BeTrue();
        dependencies[0].DownloadUrl.Should().Be("https://sp-mod.com/mod/download/902/bigbrain/1.5.0");
    }

    [Fact]
    public async Task TheGraphTurnsTheAnswersAroundAndKeepsThemOnDisk()
    {
        using var temp = new TempDirectory();
        var forge = new CatalogueForge();

        var dependents = await new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance).DependentsOfAsync(
            902,
            cancellationToken: CancellationToken.None
        );

        dependents.Select(edge => edge.FromGuid).Should().Equal("me.sol.sain", "third.mod");
        dependents.Should().OnlyContain(edge => edge.ToId == 902);
        File.Exists(temp.Combine("data", "cache", "dependency-graph.json")).Should().BeTrue();

        var asked = forge.CatalogueCalls;
        var again = await new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance).DependentsOfAsync(
            902,
            cancellationToken: CancellationToken.None
        );

        again.Should().HaveCount(2, "the answers were cached, so the catalogue is not walked twice");
        forge.CatalogueCalls.Should().Be(asked, "a cached graph reads no catalogue page");
    }

    [Fact]
    public async Task ADependencyWithoutAnIdIsNotInvented()
    {
        using var temp = new TempDirectory();
        var forge = new CatalogueForge { GhostDependency = true };

        var dependents = await new DependencyGraph(forge, temp.CreateAppPaths(), NullLogger<DependencyGraph>.Instance).DependentsOfAsync(
            902,
            cancellationToken: CancellationToken.None
        );

        // The ghost has no mod id, so it cannot be a row: it is dropped rather than shown as mod 0.
        dependents.Should().Contain(edge => edge.FromGuid == "third.mod");
        dependents.Should().OnlyContain(edge => edge.ToId == 902);
    }

    /// <summary>A catalogue small enough to reason about: two mods need BigBrain, and one of them has no id to name it by.</summary>
    private sealed class CatalogueForge : IForgeClient
    {
        public int CatalogueCalls { get; private set; }

        public bool GhostDependency { get; init; }

        public Task<ForgePing> PingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ForgePing { Message = "ok" });
        }

        public Task<ForgePage<ForgeMod>> GetModsAsync(ForgeModQuery query, CancellationToken cancellationToken = default)
        {
            CatalogueCalls++;
            var mods = new List<ForgeMod>
            {
                Mod(791, "me.sol.sain", "SAIN", "4.5.0"),
                Mod(900, "third.mod", "Third", "1.0.0"),
                Mod(902, "xyz.drakia.bigbrain", "BigBrain", "1.5.0"),
            };

            return Task.FromResult(
                new ForgePage<ForgeMod>
                {
                    Items = mods,
                    Page = 1,
                    PerPage = 50,
                    Total = mods.Count,
                    LastPage = 1,
                }
            );
        }

        public Task<ForgeMod> GetModAsync(int modId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The graph never asks for a single mod.");
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

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>> GetDependenciesAsync(
            IReadOnlyCollection<InstalledPackage> packages,
            SemanticVersion sptVersion,
            CancellationToken cancellationToken = default
        )
        {
            var answers = new Dictionary<string, IReadOnlyList<ForgeDependency>>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                answers[$"{package.Guid}:{package.Version}"] = package.Guid switch
                {
                    "me.sol.sain" => [Needs(902, "xyz.drakia.bigbrain", "BigBrain")],
                    "third.mod" => GhostDependency
                        ?
                        [
                            Needs(902, "xyz.drakia.bigbrain", "BigBrain"),
                            new ForgeDependency
                            {
                                ModId = null,
                                Guid = "ghost.mod",
                                Name = "Ghost",
                            },
                        ]
                        : [Needs(902, "xyz.drakia.bigbrain", "BigBrain")],
                    _ => [],
                };
            }

            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>>(answers);
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

        private static ForgeDependency Needs(int id, string guid, string name)
        {
            return new ForgeDependency
            {
                ModId = id,
                Guid = guid,
                Name = name,
                Version = SemanticVersion.Parse("1.5.0"),
            };
        }

        private static ForgeMod Mod(int id, string guid, string name, string version)
        {
            return new ForgeMod
            {
                Id = id,
                Guid = guid,
                Name = name,
                Slug = guid.Replace('.', '-'),
                Teaser = name,
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
    }
}
