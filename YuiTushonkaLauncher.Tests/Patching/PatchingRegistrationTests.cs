using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Patching;

namespace YuiTushonkaLauncher.Tests.Patching;

public sealed class PatchingRegistrationTests
{
    [Fact]
    public void AddLauncherPatching_RegistersTheEngineAndItsHelpersAsSingletons()
    {
        using var provider = new ServiceCollection().AddLogging().AddLauncherPatching().BuildServiceProvider();

        provider.GetRequiredService<IPatchEngine>().Should().BeOfType<SharpHDiffPatchEngine>();
        provider.GetRequiredService<IPatchEngine>().Should().BeSameAs(provider.GetRequiredService<IPatchEngine>());
        provider.GetRequiredService<DeltaSetReader>().Should().NotBeNull();
        provider.GetRequiredService<PatchPlanner>().Should().NotBeNull();
        provider.GetRequiredService<BackupScanner>().Should().NotBeNull();
    }
}
