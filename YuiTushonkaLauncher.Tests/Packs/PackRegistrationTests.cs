using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Infrastructure;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Infrastructure.Packs;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Packs;

public sealed class PackRegistrationTests
{
    [Fact]
    public void AddLauncherMods_RegistersThePackService()
    {
        using var temp = new TempDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<YuiTushonkaLauncher.Core.Abstractions.IAppPaths>(temp.CreateAppPaths());
        services.AddLauncherPersistence();
        services.AddLauncherForge();
        services.AddLauncherInstances();
        services.AddLauncherMods();

        // Packs refuse while the instance's processes run, so the pack service needs the server layer's guard (and the
        // runtime layer it uses to find the port and the runtime).
        services.AddLauncherRuntime();
        services.AddLauncherServer();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPackService>().Should().BeOfType<PackService>();
        provider.GetRequiredService<PackReader>().Should().NotBeNull();
    }
}
