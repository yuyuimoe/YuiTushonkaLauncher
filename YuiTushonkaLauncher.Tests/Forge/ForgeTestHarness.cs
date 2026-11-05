using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.Server;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>A WireMock-hosted fake Forge API plus a real <see cref="SptForgeClient" /> against it.</summary>
public sealed class ForgeTestHarness : IDisposable
{
    public ForgeTestHarness(Action<ForgeOptions>? configure = null)
    {
        Server = WireMockServer.Start();
        Settings = new ForgeOptions
        {
            BaseUrl = $"{Server.Url}/api/v0/",
            MaxAttempts = 3,
            BaseRetryDelayMilliseconds = 5,
            DefaultCacheSeconds = 60,
            BurstPermits = 100,
            SustainedPermits = 1000,
        };

        configure?.Invoke(Settings);

        var options = Microsoft.Extensions.Options.Options.Create(Settings);
        RateLimiter = new ForgeRateLimiter(options, NullLogger<ForgeRateLimiter>.Instance);
        Cache = new ForgeResponseCache(options);
        var retry = new ForgeRetryExecutor(options, NullLogger<ForgeRetryExecutor>.Instance);

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(Settings.BaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(Settings.TimeoutSeconds),
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Settings.UserAgent);
        Client = new SptForgeClient(httpClient, options, RateLimiter, retry, Cache, NullLogger<SptForgeClient>.Instance);
    }

    public WireMockServer Server { get; }

    public ForgeOptions Settings { get; }

    public ForgeRateLimiter RateLimiter { get; }

    public ForgeResponseCache Cache { get; }

    public IForgeClient Client { get; }

    public IEnumerable<string> RequestedUrls()
    {
        return Server.LogEntries.Select(entry => entry.RequestMessage!.Url);
    }

    public void Dispose()
    {
        RateLimiter.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Server.Dispose();
    }
}
