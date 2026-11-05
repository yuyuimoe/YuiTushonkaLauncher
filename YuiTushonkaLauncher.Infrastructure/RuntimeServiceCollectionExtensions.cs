using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Runtime;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers runtime discovery, profiles and prefix management.</summary>
public static class RuntimeServiceCollectionExtensions
{
    /// <summary>Adds the Proton/Wine/native runtime layer. On Windows it resolves to native and finds nothing.</summary>
    public static IServiceCollection AddLauncherRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IPlatformInfo, PlatformInfo>();
        services.AddSingleton<IExecutableFinder>(_ => new ExecutableFinder());
        services.AddSingleton<IProcessNames, ProcessNames>();
        services.AddSingleton<IRuntimeLocator>(provider => new RuntimeLocator(
            provider.GetRequiredService<IPlatformInfo>(),
            provider.GetRequiredService<IExecutableFinder>(),
            provider.GetRequiredService<IProcessNames>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RuntimeLocator>>()
        ));
        services.AddSingleton<IRuntimeLauncher, RuntimeLauncher>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPrefixService, PrefixService>();
        services.AddSingleton<IRuntimeProfileService, RuntimeProfileService>();

        return services;
    }
}
