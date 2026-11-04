using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Infrastructure;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Infrastructure.Processes;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Startup;

/// <summary>
/// The single place that knows which implementations back the Core contracts. Everything else in the
/// app depends on interfaces only.
/// </summary>
internal static class ServiceRegistration
{
    /// <summary>Registers paths, logging, services and view models.</summary>
    public static IServiceCollection AddLauncherServices(this IServiceCollection services, IAppPaths paths)
    {
        // Serilog owns the sinks; the Microsoft.Extensions.Logging bridge lets services take ILogger<T>.
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

        services.AddSingleton(paths);
        services.AddLauncherPersistence();
        services.AddLauncherForge();
        services.AddSingleton<ISingleInstanceGuard, SingleInstanceGuard>();

        services.AddSingleton<MainViewModel>();

        return services;
    }
}
