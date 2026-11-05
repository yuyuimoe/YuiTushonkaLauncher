using System.Diagnostics;
using FluentAssertions;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeRetryAndErrorTests
{
    private const string EmptyPage =
        """{ "success": true, "data": [], "meta": { "current_page": 1, "last_page": 1, "per_page": 25, "total": 0 } }""";

    [Fact]
    public async Task TransientServerErrors_AreRetried()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .InScenario("flaky")
            .WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .InScenario("flaky")
            .WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(EmptyPage));

        var page = await harness.Client.GetModsAsync(new ForgeModQuery());

        page.Items.Should().BeEmpty();
        harness.Server.LogEntries.Should().HaveCount(2, "a 503 is retried once and the second attempt succeeds");
    }

    [Fact]
    public async Task TooManyRequests_HonoursRetryAfter()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .InScenario("throttled")
            .WillSetStateTo("allowed")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .InScenario("throttled")
            .WhenStateIs("allowed")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(EmptyPage));

        var stopwatch = Stopwatch.StartNew();
        await harness.Client.GetModsAsync(new ForgeModQuery());
        stopwatch.Stop();

        harness.Server.LogEntries.Should().HaveCount(2);
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(900), "Retry-After: 1 must be respected");
    }

    [Fact]
    public async Task ClientErrors_AreNotRetried()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(400)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{ "success": false, "code": "VALIDATION_FAILED", "message": "Invalid filter(s): bogus." }""")
            );

        var act = async () => await harness.Client.GetModsAsync(new ForgeModQuery());

        var exception = await act.Should().ThrowAsync<ForgeRequestException>();
        exception.Which.Code.Should().Be("VALIDATION_FAILED");
        exception.Which.StatusCode.Should().Be(400);
        harness.Server.LogEntries.Should().HaveCount(1);
    }

    [Fact]
    public async Task NotFound_SurfacesAsANotFoundRequestException()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mod/999999").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(404)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{ "success": false, "code": "NOT_FOUND", "message": "Resource not found." }""")
            );

        var act = async () => await harness.Client.GetModAsync(999999);

        var exception = await act.Should().ThrowAsync<ForgeRequestException>();
        exception.Which.IsNotFound.Should().BeTrue();
        exception.Which.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ExhaustedRetries_SurfaceTheLastStatus()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("boom"));

        var act = async () => await harness.Client.GetModsAsync(new ForgeModQuery());

        await act.Should().ThrowAsync<ForgeRequestException>();
        harness.Server.LogEntries.Should().HaveCount(3, "MaxAttempts is 3 in the test harness");
    }

    [Fact]
    public async Task MalformedJson_SurfacesAsAForgeException()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{ not json"));

        var act = async () => await harness.Client.GetModsAsync(new ForgeModQuery());

        await act.Should().ThrowAsync<ForgeException>();
    }

    [Fact]
    public async Task SuccessFalseEnvelope_IsRejectedEvenWithHttp200()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/ping").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{ "success": false, "code": "MAINTENANCE", "message": "Down for maintenance." }""")
            );

        var act = async () => await harness.Client.PingAsync();

        var exception = await act.Should().ThrowAsync<ForgeRequestException>();
        exception.Which.Code.Should().Be("MAINTENANCE");
    }

    [Fact]
    public async Task Responses_AreCachedForTheirMaxAge()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithHeader("Cache-Control", "max-age=900, public")
                    .WithBody(EmptyPage)
            );

        await harness.Client.GetModsAsync(new ForgeModQuery());
        await harness.Client.GetModsAsync(new ForgeModQuery());

        harness.Server.LogEntries.Should().HaveCount(1);
        harness.Cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task QuotaHeader_IsObservedAndCachedResponsesSurvive()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/ping").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithHeader("x-ratelimit-limit", "300")
                    .WithHeader("x-ratelimit-remaining", "299")
                    .WithBody("""{ "success": true, "data": { "message": "pong" } }""")
            );

        var ping = await harness.Client.PingAsync();

        ping.Message.Should().Be("pong");
        harness.Cache.TryGet("ping", out var cached).Should().BeTrue();
        cached.Should().Contain("pong");
    }

    [Fact]
    public void Clear_DropsEverything()
    {
        using var harness = new ForgeTestHarness();
        harness.Cache.Set("key", "body", TimeSpan.FromMinutes(5));

        harness.Cache.Clear();

        harness.Cache.TryGet("key", out _).Should().BeFalse();
        harness.Cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task RateLimiter_BlocksWhenTheBurstWindowIsExhausted()
    {
        using var harness = new ForgeTestHarness(options =>
        {
            options.BurstPermits = 1;
            options.BurstWindowSeconds = 1;
            options.SustainedPermits = 1000;
        });

        var stopwatch = Stopwatch.StartNew();
        await harness.RateLimiter.WaitAsync();
        var first = stopwatch.Elapsed;
        await harness.RateLimiter.WaitAsync();
        stopwatch.Stop();

        first.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(700));
    }
}
