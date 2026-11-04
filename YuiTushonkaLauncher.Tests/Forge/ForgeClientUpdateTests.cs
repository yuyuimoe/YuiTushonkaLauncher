using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeClientUpdateTests
{
    private const string UpdatesJson = """
        {
          "success": true,
          "data": {
            "spt_version": "4.1.6",
            "updates": [
              {
                "current_version": { "id": 5907, "mod_id": 827, "guid": "xyz.drakia.waypoints",
                                     "name": "Waypoints - Expanded Navmesh", "slug": "waypoints", "version": "1.3.1" },
                "recommended_version": { "id": 14423, "version": "1.9.0",
                                         "link": "https://sp-mod.com/mod/download/827/waypoints/1.9.0",
                                         "content_length": 50349566, "fika_compatibility": "unknown",
                                         "spt_versions": ["4.1.6", "4.1.5"] }
              }
            ],
            "blocked_updates": [
              {
                "current_version": { "id": 1, "mod_id": 900, "guid": "com.x.blocked", "name": "Blocked", "version": "2.0.0" },
                "recommended_version": { "id": 2, "version": "3.0.0", "link": "https://x/3.0.0" }
              }
            ],
            "up_to_date": [
              { "id": 3, "mod_id": 31, "guid": "com.donut.scavcat", "name": "Scav Cat", "version": "1.0.8", "spt_versions": ["4.0.10"] }
            ],
            "incompatible_with_spt": [
              { "id": 4, "mod_id": 31, "guid": "com.donut.scavcat", "name": "Scav Cat", "version": "1.0.0",
                "reason": "no_version_for_spt", "latest_compatible_version": null }
            ]
          }
        }
        """;

    [Fact]
    public async Task CheckUpdatesAsync_MapsAllFourBuckets()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods/updates").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(UpdatesJson));

        var report = await harness.Client.CheckUpdatesAsync(
            [new InstalledPackage { Guid = "xyz.drakia.waypoints", Version = SemanticVersion.Parse("1.3.1") }],
            SemanticVersion.Parse("4.1.6")
        );

        report.SptVersion.Should().Be(SemanticVersion.Parse("4.1.6"));
        report.Updates.Should().ContainSingle();
        report.Updates[0].Current.Guid.Should().Be("xyz.drakia.waypoints");
        report.Updates[0].Recommended.Version.Should().Be(SemanticVersion.Parse("1.9.0"));
        report.Updates[0].Recommended.SupportedSptVersions.Should().HaveCount(2);

        report.BlockedUpdates.Should().ContainSingle().Which.Guid.Should().Be("com.x.blocked");
        report.UpToDate.Should().ContainSingle().Which.Version.Should().Be(SemanticVersion.Parse("1.0.8"));
        report.Incompatible.Should().ContainSingle();
        report.Incompatible[0].Reason.Should().Be("no_version_for_spt");
        report.IsClean.Should().BeFalse();

        // The API wants "identifier:version" pairs plus the SPT version, not a body.
        var query = harness.Server.LogEntries.Single().RequestMessage!.Query!;
        query.Should().ContainKey("mods").WhoseValue.Should().Equal("xyz.drakia.waypoints:1.3.1");
        query.Should().ContainKey("spt_version").WhoseValue.Should().Equal("4.1.6");
    }

    [Fact]
    public async Task CheckUpdatesAsync_WithNoInstalledMods_DoesNotCallTheApi()
    {
        using var harness = new ForgeTestHarness();

        var report = await harness.Client.CheckUpdatesAsync([], SemanticVersion.Parse("4.1.6"));

        report.IsClean.Should().BeTrue();
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDependenciesAsync_ReturnsThePerPackageMap()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods/dependencies").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": {
                            "827:1.9.0": [
                              { "mod_id": 900, "guid": "com.some.dependency", "name": "Dependency", "version": "1.2.3", "is_optional": false }
                            ]
                          } }
                        """
                    )
            );

        var map = await harness.Client.GetDependenciesAsync(
            [new InstalledPackage { Guid = "xyz.drakia.waypoints", Version = SemanticVersion.Parse("1.9.0") }],
            SemanticVersion.Parse("4.1.6")
        );

        map.Should().ContainKey("827:1.9.0");
        map["827:1.9.0"].Should().ContainSingle();
        map["827:1.9.0"][0].Guid.Should().Be("com.some.dependency");
        map["827:1.9.0"][0].Version.Should().Be(SemanticVersion.Parse("1.2.3"));
    }

    [Fact]
    public async Task CheckUpdatesAsync_ChunksLargeInstalledSets()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods/updates").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """{ "success": true, "data": { "spt_version": "4.1.6", "updates": [], "blocked_updates": [], "up_to_date": [], "incompatible_with_spt": [] } }"""
                    )
            );

        var installed = Enumerable
            .Range(1, 41)
            .Select(index => new InstalledPackage { Guid = $"com.example.mod{index}", Version = SemanticVersion.Parse("1.0.0") })
            .ToList();

        await harness.Client.CheckUpdatesAsync(installed, SemanticVersion.Parse("4.1.6"));

        // 41 packages exceed the 40-per-request chunk, so exactly two calls are expected.
        harness.Server.LogEntries.Should().HaveCount(2);
    }
}
