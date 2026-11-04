using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Patching;

namespace YuiTushonkaLauncher.Patching;

/// <summary>Registers the delta reader, the patch engine and the planner.</summary>
public static class PatchingServiceCollectionExtensions
{
    /// <summary>Adds binary patching (the <c>IPatchService</c> itself is registered with the instances).</summary>
    public static IServiceCollection AddLauncherPatching(this IServiceCollection services)
    {
        services.AddSingleton<IPatchEngine, SharpHDiffPatchEngine>();
        services.AddSingleton<DeltaSetReader>();
        services.AddSingleton<PatchPlanner>();
        services.AddSingleton<BackupScanner>();

        return services;
    }
}
