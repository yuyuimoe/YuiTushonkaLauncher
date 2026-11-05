using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>Registers the Forge API client, its limiter, cache and downloader.</summary>
public static class ForgeServiceCollectionExtensions
{
    /// <summary>Adds Forge API access. Options can be overridden through <see cref="ForgeOptions" />.</summary>
    public static IServiceCollection AddLauncherForge(this IServiceCollection services)
    {
        services.AddOptions<ForgeOptions>();
        services.AddLogging();

        services.AddSingleton<ForgeRateLimiter>();
        services.AddSingleton<ForgeRetryExecutor>();
        services.AddSingleton<ForgeResponseCache>();

        services
            .AddHttpClient<IForgeClient, SptForgeClient>(
                (provider, client) => ConfigureApiClient(client, provider.GetRequiredService<IOptions<ForgeOptions>>().Value)
            )
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services
            .AddHttpClient<IForgeDownloader, ForgeDownloader>(
                (provider, client) =>
                {
                    var options = provider.GetRequiredService<IOptions<ForgeOptions>>().Value;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);

                    // A single archive can take a long time; cancellation is the caller's job.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 });

        services.AddSingleton<ForgeSptVersionCatalog>();

        // Who needs whom: the API has no reverse lookup, so the answers it does give are kept and turned around.
        services.AddSingleton<IDependencyGraph, DependencyGraph>();

        // The page reader asks the site, not the API, for the three things the API never sends.
        services.AddSingleton<IForgePageReader>(provider =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TushonkaLauncher/1.0");
            return new ForgePageReader(
                client,
                provider.GetRequiredService<ILogger<ForgePageReader>>(),
                provider.GetRequiredService<IOptions<ForgeOptions>>().Value.SiteUrl
            );
        });
        services.AddSingleton<IThumbnailCache, ThumbnailCache>();

        return services;
    }

    private static void ConfigureApiClient(HttpClient client, ForgeOptions options)
    {
        var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";
        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }
}
