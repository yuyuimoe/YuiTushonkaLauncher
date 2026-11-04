using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Infrastructure.Launch;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers the launch pipeline and the guard that keeps file-rewriting steps away from a live instance.</summary>
public static class LaunchServiceCollectionExtensions
{
    /// <summary>Adds the pipeline. Needs the instance, patch, runtime and server services.</summary>
    public static IServiceCollection AddLauncherLaunch(this IServiceCollection services)
    {
        services.AddSingleton<IInstanceBusyGuard, InstanceBusyGuard>();
        services.AddSingleton<ILaunchService, LaunchService>();

        return services;
    }
}
