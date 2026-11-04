using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeRetryTransportTests
{
    private const string EmptyPage =
        """{ "success": true, "data": [], "meta": { "current_page": 1, "last_page": 1, "per_page": 25, "total": 0 } }""";

    [Fact]
    public async Task TransportFailure_SurfacesAsUnavailableAfterRetrying()
    {
        using var harness = new ForgeTestHarness(options =>
        {
            options.MaxAttempts = 2;
            options.BaseRetryDelayMilliseconds = 5;
        });

        var retry = new ForgeRetryExecutor(
            Microsoft.Extensions.Options.Options.Create(harness.Settings),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ForgeRetryExecutor>.Instance
        );

        // Port 1 is never listening: every attempt fails at the transport layer.
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/"), Timeout = TimeSpan.FromSeconds(5) };

        var act = async () =>
            await retry.SendAsync(
                token => httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "ping"), token),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<ForgeUnavailableException>();
    }

    [Fact]
    public async Task Timeout_IsRetriedAndThenSurfaced()
    {
        using var harness = new ForgeTestHarness(options =>
        {
            options.TimeoutSeconds = 1;
            options.MaxAttempts = 2;
            options.BaseRetryDelayMilliseconds = 5;
        });

        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody(EmptyPage));

        var act = async () => await harness.Client.GetModsAsync(new ForgeModQuery());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RetryAfterAsDate_IsHonoured()
    {
        using var harness = new ForgeTestHarness();
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .InScenario("date-throttle")
            .WillSetStateTo("allowed")
            .RespondWith(
                Response.Create().WithStatusCode(429).WithHeader("Retry-After", DateTimeOffset.UtcNow.AddSeconds(1).ToString("R"))
            );
        harness
            .Server.Given(Request.Create().WithPath("/api/v0/mods").UsingGet())
            .InScenario("date-throttle")
            .WhenStateIs("allowed")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(EmptyPage));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await harness.Client.GetModsAsync(new ForgeModQuery());
        stopwatch.Stop();

        harness.Server.LogEntries.Should().HaveCount(2);
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(500));
    }

    [Theory]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(200, false)]
    public void IsRetryable_ClassifiesStatuses(int statusCode, bool expected)
    {
        ForgeRetryExecutor.IsRetryable((System.Net.HttpStatusCode)statusCode).Should().Be(expected);
    }
}
