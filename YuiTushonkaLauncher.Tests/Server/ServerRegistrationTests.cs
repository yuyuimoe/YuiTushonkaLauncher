using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerRegistrationTests
{
    private static ServiceProvider Build(TempDirectory temp)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAppPaths>(temp.CreateAppPaths());
        services.AddLauncherPersistence();
        services.AddLauncherForge();
        services.AddLauncherInstances();
        services.AddLauncherMods();
        services.AddLauncherRuntime();
        services.AddLauncherServer();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddLauncherServer_RegistersEveryServiceOnce()
    {
        using var temp = new TempDirectory();
        using var provider = Build(temp);

        provider.GetRequiredService<ISptServerLocator>().Should().BeOfType<SptServerLocator>();
        provider.GetRequiredService<ISptServerSupervisor>().Should().BeOfType<SptServerSupervisor>();
        provider.GetRequiredService<ISptServerClientFactory>().Should().BeOfType<SptServerClientFactory>();
        provider.GetRequiredService<IServerProfileService>().Should().BeOfType<ServerProfileService>();
        provider.GetRequiredService<IServerPortService>().Should().BeOfType<ServerPortService>();
        provider.GetRequiredService<IServerSyncService>().Should().BeOfType<ServerSyncService>();
        provider.GetRequiredService<IChildProcessSpawner>().Should().BeOfType<ChildProcessSpawner>();
        provider.GetRequiredService<IProcessInspector>().Should().BeOfType<ProcessInspector>();
        provider.GetRequiredService<IDotNetRuntimeProbe>().Should().BeOfType<DotNetRuntimeProbe>();
        provider.GetRequiredService<IPortProbe>().Should().BeOfType<PortProbe>();
        provider.GetRequiredService<ISptServerSupervisor>().Should().BeSameAs(provider.GetRequiredService<ISptServerSupervisor>());
        provider.GetRequiredService<IChildProcessSpawner>().Should().BeSameAs(provider.GetRequiredService<IChildProcessSpawner>());
    }

    [Fact]
    public async Task TheSupervisorAndTheSpawner_AreDisposedWithTheContainer()
    {
        using var temp = new TempDirectory();
        var provider = Build(temp);
        var supervisor = provider.GetRequiredService<ISptServerSupervisor>();
        var spawner = provider.GetRequiredService<IChildProcessSpawner>();

        await provider.DisposeAsync();

        // Both own something that must end with the launcher: servers to stop, and a thread that starts them.
        supervisor.Should().BeAssignableTo<IAsyncDisposable>().And.BeAssignableTo<IDisposable>();
        spawner.Should().BeAssignableTo<IAsyncDisposable>().And.BeAssignableTo<IDisposable>();
        var act = () => spawner.Start(new ChildProcessStart { Command = new LaunchCommand { FileName = "/bin/true" } });
        act.Should().Throw<Exception>("a disposed spawner must not start anything");
    }
}
