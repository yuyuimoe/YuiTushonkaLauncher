using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Mods;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers mod identity reading, archive handling, installation and integrity.</summary>
public static class ModServiceCollectionExtensions
{
    /// <summary>Adds the mod pipeline on top of the file and persistence layers.</summary>
    public static IServiceCollection AddLauncherMods(this IServiceCollection services)
    {
        services.AddSingleton<IModIdentityReader, ModIdentityReader>();
        services.AddSingleton<IArchiveInspector, ArchiveInspector>();
        services.AddSingleton<IArchiveExtractor, SafeExtractor>();
        services.AddSingleton<IModInstaller, ModInstaller>();
        services.AddSingleton<IIntegrityService, IntegrityService>();

        return services;
    }
}
