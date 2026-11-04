using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Updates;

namespace YuiTushonkaLauncher.Infrastructure.Updates;

public static class UpdateServiceCollectionExtensions
{
    /// <summary>Registers the release check. It uses its own client: one request, no retries, no shared rate limiter.</summary>
    public static IServiceCollection AddLauncherUpdates(this IServiceCollection services, string currentVersion)
    {
        services.AddSingleton<IUpdateChecker>(provider =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("YuiTushonkaLauncher/" + currentVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return new GitHubReleaseChecker(http, provider.GetRequiredService<ILogger<GitHubReleaseChecker>>(), currentVersion);
        });

        return services;
    }
}
