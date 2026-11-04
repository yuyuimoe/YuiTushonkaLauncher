using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeClientQueryTests
{
    private const string ModsPageJson = """
        {
          "success": true,
          "data": [
            {
              "id": 31,
              "hub_id": 76,
              "guid": "com.donut.scavcat",
              "name": "Scav Cat Trader Mod",
              "slug": "scav-cat-trader-mod",
              "teaser": "Scav Cat sells cases.",
              "thumbnail": "https://files.sp-mod.com/mods/76.jpg",
              "downloads": 13843,
              "favourites_count": 10,
              "category_id": 16,
              "featured": false,
              "fika_compatibility": false,
              "published_at": "2021-01-01T07:22:00.000000Z",
              "updated_at": "2026-07-28T07:26:05.000000Z",
              "owner": { "name": "DonutxLord" },
              "versions": [
                {
                  "id": 12710,
                  "version": "1.0.8",
                  "link": "https://sp-mod.com/mod/download/31/scav-cat-trader-mod/1.0.8",
                  "content_length": 43815,
                  "spt_version_constraint": " 4.0.10 ",
                  "downloads": 4979,
                  "fika_compatibility": "unknown",
                  "published_at": "2025-12-29T20:52:00.000000Z"
                }
              ]
            }
          ],
          "meta": { "current_page": 1, "last_page": 83, "per_page": 25, "total": 2077 }
        }
        """;

    [Fact]
    public async Task GetModsAsync_SendsTheDocumentedQueryShapeAndMapsThePage()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(ModsPageJson));

        var page = await harness.Client.GetModsAsync(
            new ForgeModQuery
            {
                Guids = ["com.donut.scavcat"],
                Search = "scav",
                SptVersion = SemanticVersion.Parse("4.1.6"),
                IncludeVersions = true,
                Sort = "-downloads",
                Page = 2,
                PerPage = 25,
            }
        );

        var url = harness.RequestedUrls().Single();
        var query = harness.Server.LogEntries.Single().RequestMessage!.Query!;

        // Filters use Laravel's filter[name] syntax, percent-encoded on the wire; ?guids= and ?ids=
        // are silently ignored by the API, so this shape is what has to be sent.
        url.Should().Contain("filter%5Bguid%5D=com.donut.scavcat");
        url.Should().Contain("filter%5Bspt_version%5D=4.1.6");
        query.Should().ContainKey("query").WhoseValue.Should().Equal("scav");
        query.Should().ContainKey("include").WhoseValue.Should().Equal("versions");
        query.Should().ContainKey("sort").WhoseValue.Should().Equal("-downloads");
        query.Should().ContainKey("page").WhoseValue.Should().Equal("2");
        query.Should().ContainKey("per_page").WhoseValue.Should().Equal("25");

        page.Total.Should().Be(2077);
        page.LastPage.Should().Be(83);
        page.Items.Should().ContainSingle();
        var mod = page.Items[0];
        mod.Id.Should().Be(31);
        mod.Guid.Should().Be("com.donut.scavcat");
        mod.Author.Should().Be("DonutxLord");
        mod.Versions.Should().ContainSingle();
        mod.Versions[0].Version.Should().Be(SemanticVersion.Parse("1.0.8"));
        // The API really does publish constraints with stray whitespace; it must parse anyway.
        mod.Versions[0].SptConstraint.Raw.Should().Be("4.0.10");
        mod.Versions[0].SptConstraint.Satisfies(SemanticVersion.Parse("4.0.10")).Should().BeTrue();
    }

    [Fact]
    public async Task GetModVersionsAsync_UsesTheSingularModPathAndFollowsPagination()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mod/827/versions").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": [
                            { "id": 1, "version": "1.9.0", "link": "https://x/1.9.0", "spt_version_constraint": "^4.1.0" }
                          ], "meta": { "current_page": 1, "last_page": 1, "per_page": 100, "total": 1 } }
                        """
                    )
            );

        var versions = await harness.Client.GetModVersionsAsync(827);

        harness.RequestedUrls().Single().Should().Contain("/api/v0/mod/827/versions");
        versions.Should().ContainSingle();
        versions[0].Version.Should().Be(SemanticVersion.Parse("1.9.0"));
        versions[0].SptConstraint.Satisfies(SemanticVersion.Parse("4.1.6")).Should().BeTrue();
    }

    [Fact]
    public async Task FindModsByGuidsAsync_FiltersLocallyToExactGuids()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": [
                            { "id": 31, "guid": "com.donut.scavcat", "name": "Scav Cat", "slug": "scav-cat" },
                            { "id": 32, "guid": "someone.else.mod", "name": "Other", "slug": "other" }
                          ], "meta": { "current_page": 1, "last_page": 1, "per_page": 100, "total": 2 } }
                        """
                    )
            );

        var mods = await harness.Client.FindModsByGuidsAsync(["COM.DONUT.SCAVCAT"]);

        mods.Should().ContainSingle();
        mods[0].Guid.Should().Be("com.donut.scavcat");
    }

    [Fact]
    public async Task GetSptVersionsAsync_MapsReleaseMetadata()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/spt/versions").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": [
                            { "id": 55, "version": "4.1.6", "mod_count": 738,
                              "link": "https://github.com/SP-Tushonka/build/releases/tag/4.1.6",
                              "updated_at": "2026-11-04T02:35:06.000000Z" }
                          ], "meta": { "current_page": 1, "last_page": 1, "per_page": 50, "total": 1 } }
                        """
                    )
            );

        var releases = await harness.Client.GetSptVersionsAsync();

        releases.Should().ContainSingle();
        releases[0].Version.Should().Be(SemanticVersion.Parse("4.1.6"));
        releases[0].ModCount.Should().Be(738);
        releases[0].ReleaseUrl.Should().Contain("SP-Tushonka/build");
    }
}
