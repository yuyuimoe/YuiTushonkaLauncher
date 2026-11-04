using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeClientCatalogTests
{
    [Fact]
    public async Task PingAsync_ReturnsTheApiMessage()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/ping").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{ "success": true, "data": { "message": "pong" } }""")
            );

        var ping = await harness.Client.PingAsync();

        ping.Message.Should().Be("pong");
    }

    [Fact]
    public async Task GetCategoriesAsync_MapsIdTitleAndSlug()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mod-categories").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": [
                            { "id": 12, "hub_id": 75, "title": "Audio", "slug": "audio", "description": "" },
                            { "id": 5, "hub_id": 22, "title": "Bots", "slug": "bots", "description": "" }
                          ], "meta": { "current_page": 1, "last_page": 1, "per_page": 100, "total": 2 } }
                        """
                    )
            );

        var categories = await harness.Client.GetCategoriesAsync();

        categories.Should().HaveCount(2);
        categories[0].Title.Should().Be("Audio");
        categories[1].Slug.Should().Be("bots");
    }

    [Fact]
    public async Task GetAddonsAsync_MapsAddonsAndFiltersByParentMod()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/addons").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": [
                            { "id": 179, "mod_id": 3290, "name": "Raid Roulette", "slug": "raid-roulette",
                              "is_detached": false, "downloads": 2, "published_at": "2026-11-03T21:40:00.000000Z" },
                            { "id": 180, "mod_id": 42, "name": "Other", "slug": "other", "is_detached": true, "downloads": 0 }
                          ], "meta": { "current_page": 1, "last_page": 139, "per_page": 25, "total": 2 } }
                        """
                    )
            );

        var page = await harness.Client.GetAddonsAsync(new ForgeAddonQuery { ModId = 3290, Search = "roulette" });

        page.Items.Should().ContainSingle();
        page.Items[0].Name.Should().Be("Raid Roulette");
        page.Items[0].ModId.Should().Be(3290);
        page.LastPage.Should().Be(139);
    }

    [Fact]
    public async Task GetAddonVersionsAsync_MapsTheModVersionConstraint()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/addon/179/versions").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": [
                            { "id": 380, "version": "1.0.0", "link": "https://sp-mod.com/mod/download/addon/179/1.0.0",
                              "content_length": 1024, "mod_version_constraint": "^2.0.0", "downloads": 5,
                              "published_at": "2026-11-03T21:44:34.000000Z" }
                          ], "meta": { "current_page": 1, "last_page": 1, "per_page": 25, "total": 1 } }
                        """
                    )
            );

        var versions = await harness.Client.GetAddonVersionsAsync(179);

        versions.Should().ContainSingle();
        versions[0].Version.Should().Be(SemanticVersion.Parse("1.0.0"));
        // Addons constrain the parent mod version, not the SPT version.
        versions[0].ModVersionConstraint.Satisfies(SemanticVersion.Parse("2.4.1")).Should().BeTrue();
        versions[0].ModVersionConstraint.Satisfies(SemanticVersion.Parse("3.0.0")).Should().BeFalse();
    }

    [Fact]
    public async Task GetModAsync_MapsModLevelDetail()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mod/31").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        { "success": true, "data": {
                            "id": 31, "hub_id": 76, "guid": "com.donut.scavcat", "name": "Scav Cat Trader Mod",
                            "slug": "scav-cat-trader-mod", "description": "<p>Some HTML</p>", "downloads": 13844,
                            "favourites_count": 10, "featured": false, "fika_compatibility": false,
                            "owner": { "name": "DonutxLord" } } }
                        """
                    )
            );

        var mod = await harness.Client.GetModAsync(31);

        mod.Id.Should().Be(31);
        mod.Description.Should().Contain("Some HTML");
        mod.Author.Should().Be("DonutxLord");
        mod.Versions.Should().BeEmpty();
    }

    [Fact]
    public async Task UnparsableVersionInThePayload_SurfacesAsAForgeException()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mod/1/versions").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{ "success": true, "data": [ { "id": 1, "version": "not-a-version", "link": "https://x" } ] }""")
            );

        var act = async () => await harness.Client.GetModVersionsAsync(1);

        await act.Should().ThrowAsync<ForgeException>().WithMessage("*not-a-version*");
    }
}
