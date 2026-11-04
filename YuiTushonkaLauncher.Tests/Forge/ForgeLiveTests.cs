using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// Opt-in tests against the real API. They are skipped by default so CI never depends on the network:
/// set <c>YTT_LIVE_FORGE=1</c> to run them (`dotnet test --filter Live=Forge`).
/// </summary>
[Trait("Live", "Forge")]
public sealed class ForgeLiveTests
{
    private static bool Enabled
    {
        get { return Environment.GetEnvironmentVariable("YTT_LIVE_FORGE") == "1"; }
    }

    [Fact]
    public async Task LiveApi_AnswersPingModsAndSptVersions()
    {
        if (!Enabled)
        {
            return;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var settings = new ForgeOptions();
        var options = Options.Create(settings);
        using var rateLimiter = new ForgeRateLimiter(options, NullLogger<ForgeRateLimiter>.Instance);
        var retry = new ForgeRetryExecutor(options, NullLogger<ForgeRetryExecutor>.Instance);
        var cache = new ForgeResponseCache(options);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);

        var client = new SptForgeClient(
            httpClient,
            options,
            rateLimiter,
            retry,
            new ForgeResponseCache(options),
            NullLogger<SptForgeClient>.Instance
        );
        httpClient.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);

        var ping = await client.PingAsync();
        ping.Message.Should().Be("pong");

        var page = await client.GetModsAsync(new ForgeModQuery { PerPage = 1 });
        page.Total.Should().BeGreaterThan(0);
        page.Items.Should().HaveCount(1);

        var releases = await client.GetSptVersionsAsync(perPage: 5);
        releases.Should().NotBeEmpty();
        releases.Should().OnlyContain(release => release.Version.Major >= 3);

        // Keep the cache alive so the happy-path assertions also cover the cached read.
        cache.Clear();
    }
}
