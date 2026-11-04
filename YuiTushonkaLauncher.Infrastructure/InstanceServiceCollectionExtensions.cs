using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Patching;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers the file layer, the instance services and binary patching.</summary>
public static class InstanceServiceCollectionExtensions
{
    /// <summary>Adds hard links, copy-on-write, layout detection, instances, migration and patching.</summary>
    public static IServiceCollection AddLauncherInstances(this IServiceCollection services)
    {
        services.AddSingleton<IFileLinkService, FileLinkService>();
        services.AddSingleton<ILinkAwareFileWriter, LinkAwareFileWriter>();
        services.AddSingleton<BaseInstallProbe>();
        services.AddSingleton<PatchStateDetector>();
        services.AddSingleton<IInstanceService, InstanceService>();
        services.AddSingleton<IMigrationService, MigrationService>();

        // The engine, the delta reader and the planner live in the Patching assembly; the service below
        // is what binds them to an instance and to the database.
        services.AddLauncherPatching();
        services.AddSingleton<IPatchService, PatchingService>();

        return services;
    }
}
