using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Infrastructure.Logs;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers the log layer: the catalog of sources, the tail reader, the exporter and retention.</summary>
public static class LogServiceCollectionExtensions
{
    /// <summary>Adds the log services. Needs the persistence layer (for the instance row).</summary>
    public static IServiceCollection AddLauncherLogs(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILogCatalog, LogCatalog>();
        services.AddSingleton<ILogReader, LogReader>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<ILogRetention, LogRetention>();

        return services;
    }
}
