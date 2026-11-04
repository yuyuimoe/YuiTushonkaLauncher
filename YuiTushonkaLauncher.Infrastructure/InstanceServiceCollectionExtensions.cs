using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers the file layer and the instance services.</summary>
public static class InstanceServiceCollectionExtensions
{
    /// <summary>Adds hard links, copy-on-write, layout detection and the instance service.</summary>
    public static IServiceCollection AddLauncherInstances(this IServiceCollection services)
    {
        services.AddSingleton<IFileLinkService, FileLinkService>();
        services.AddSingleton<ILinkAwareFileWriter, LinkAwareFileWriter>();
        services.AddSingleton<BaseInstallProbe>();
        services.AddSingleton<PatchStateDetector>();
        services.AddSingleton<IInstanceService, InstanceService>();
        services.AddSingleton<IMigrationService, MigrationService>();

        return services;
    }
}
