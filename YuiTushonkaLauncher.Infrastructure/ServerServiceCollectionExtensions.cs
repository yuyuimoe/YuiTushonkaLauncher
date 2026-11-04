using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers the local SPT server layer: locating, supervising, the launcher API, profiles, ports and sync.</summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>Adds the server layer. Needs the instance, runtime, mod and Forge services.</summary>
    public static IServiceCollection AddLauncherServer(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISptServerLocator, SptServerLocator>();
        services.AddSingleton<IPortProbe, PortProbe>();
        services.AddSingleton<IDotNetRuntimeProbe, DotNetRuntimeProbe>();
        services.AddSingleton<IProcessInspector, ProcessInspector>();
        services.AddSingleton<IScopeService, SystemdScopeService>();
        services.AddSingleton<ISptServerClientFactory, SptServerClientFactory>();
        services.AddSingleton<IInstanceRunStore, InstanceRunStore>();
        services.AddSingleton<IRunAdoptionService, RunAdoptionService>();

        // The spawner owns a thread that never ends (it is what the children's lifetime is tied to), so it must be disposed with the container.
        services.AddSingleton<IChildProcessSpawner>(provider => new ChildProcessSpawner(
            provider.GetRequiredService<IPlatformInfo>(),
            provider.GetRequiredService<IExecutableFinder>(),
            provider.GetRequiredService<IProcessRunner>(),
            provider.GetRequiredService<ILogger<ChildProcessSpawner>>()
        ));

        services.AddSingleton<ISptServerSupervisor, SptServerSupervisor>();
        services.AddSingleton<IServerProfileService, ServerProfileService>();
        services.AddSingleton<IServerPortService, ServerPortService>();
        services.AddSingleton<IServerSyncService, ServerSyncService>();

        // Refusing a migration while the server runs needs to know the server state, so it lives here rather than with
        // the instance services.
        services.AddSingleton<Core.Instances.ILayoutMigrationService, Instances.LayoutMigrationService>();

        return services;
    }
}
