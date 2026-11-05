using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class SptServerLocatorTests : IDisposable
{
    private readonly ServerWorld _world = new();

    private static SptServerLocator Linux()
    {
        return new SptServerLocator(new FakePlatform(linux: true));
    }

    public void Dispose()
    {
        _world.Dispose();
    }

    [Fact]
    public void Locate_FindsTheLinuxServerAndItsPort()
    {
        var instance = _world.CreateInstance("one", port: 6975);

        var location = Linux().Locate(instance);

        location.IsComplete.Should().BeTrue();
        location.ExecutablePath.Should().EndWith("SPT_Runtime/SPT.Server.Linux");
        location.WorkingDirectory.Should().EndWith("SPT_Runtime");
        location.Endpoint.Port.Should().Be(6975);
        location.ProfilesDirectory.Should().EndWith(Path.Combine("user", "profiles"));
    }

    [Fact]
    public void Locate_UsesTheWindowsExecutableOnWindows()
    {
        var instance = _world.CreateInstance("one");

        new SptServerLocator(new FakePlatform(linux: false)).Locate(instance).ExecutablePath.Should().EndWith("SPT.Server.exe");
    }

    [Fact]
    public void Locate_UsesTheFolderOfTheOlderLayout()
    {
        var instance = _world.CreateInstance("old", layout: InstanceLayout.Spt40);

        var location = Linux().Locate(instance);

        location.IsComplete.Should().BeTrue();
        location.WorkingDirectory.Should().EndWith("SPT");
    }

    [Fact]
    public void Locate_ReadsTheRuntimeConfigThatBelongsToTheLinuxBuild()
    {
        // Regression: Path.ChangeExtension("SPT.Server.Linux", null) is "SPT.Server", which read the Windows file.
        // The two files are identical in a real install, so the bug hid until one of them was changed.
        var instance = _world.CreateInstance("one");
        var runtime = ServerWorld.RuntimeOf(instance);
        File.WriteAllText(
            Path.Combine(runtime, "SPT.Server.Linux.runtimeconfig.json"),
            ServerWorld.RuntimeConfig.Replace("10.0.0", "11.0.0")
        );

        Linux()
            .Locate(instance)
            .Requirements.Should()
            .BeEquivalentTo([
                new FrameworkRequirement("Microsoft.NETCore.App", "11.0.0"),
                new FrameworkRequirement("Microsoft.AspNetCore.App", "11.0.0"),
            ]);
        new SptServerLocator(new FakePlatform(linux: false))
            .Locate(instance)
            .Requirements.Select(item => item.Version)
            .Should()
            .OnlyContain(version => version == "10.0.0");
    }

    [Fact]
    public void Locate_ReportsAMissingExecutable_WithItsPath()
    {
        var instance = _world.CreateInstance("one");
        File.Delete(Path.Combine(ServerWorld.RuntimeOf(instance), "SPT.Server.Linux"));

        var location = Linux().Locate(instance);

        location.IsComplete.Should().BeFalse();
        location.ExecutablePath.Should().BeNull();
        location
            .Problems.Should()
            .ContainSingle(problem => problem.Code == ServerProblemCode.ExecutableMissing && problem.Path!.EndsWith("SPT.Server.Linux"));
    }

    [Fact]
    public void Locate_ReportsAMissingServerFolder()
    {
        var instance = _world.CreateInstance("one");
        Directory.Delete(ServerWorld.RuntimeOf(instance), recursive: true);

        Linux().Locate(instance).Problems.Should().Contain(problem => problem.Code == ServerProblemCode.WorkingDirectoryMissing);
    }

    [Fact]
    public void Locate_ReportsAMissingOrBrokenConfig_AndStillAnswersWithTheDefaultPort()
    {
        var instance = _world.CreateInstance("one");
        File.Delete(ServerWorld.HttpConfigOf(instance));

        var missing = Linux().Locate(instance);

        missing.Problems.Should().Contain(problem => problem.Code == ServerProblemCode.ConfigMissing);
        missing.Endpoint.Port.Should().Be(6969);

        File.WriteAllText(ServerWorld.HttpConfigOf(instance), "{nope");

        Linux()
            .Locate(instance)
            .Problems.Should()
            .Contain(problem => problem.Code == ServerProblemCode.ConfigInvalid && problem.Message.Contains("not valid JSON"));
    }

    [Fact]
    public void Locate_WarnsWhenTheServerListensOnTheWholeNetwork()
    {
        var wide = _world.CreateInstance("wide", ip: "0.0.0.0");
        var narrow = _world.CreateInstance("narrow", ip: "127.0.0.1");

        var warning = Linux()
            .Locate(wide)
            .Problems.Should()
            .ContainSingle(problem => problem.Code == ServerProblemCode.ListensOnAllInterfaces)
            .Subject;

        warning.IsError.Should().BeFalse("it is advice, not a reason to refuse");
        Linux().Locate(wide).IsComplete.Should().BeTrue();
        Linux().Locate(narrow).Problems.Should().NotContain(problem => problem.Code == ServerProblemCode.ListensOnAllInterfaces);
    }

    [Fact]
    public void Locate_GivesNoRequirements_WhenTheRuntimeConfigIsMissingOrBroken()
    {
        var instance = _world.CreateInstance("one");
        var config = Path.Combine(ServerWorld.RuntimeOf(instance), "SPT.Server.Linux.runtimeconfig.json");

        File.WriteAllText(config, "{broken");
        Linux().Locate(instance).Requirements.Should().BeEmpty();

        File.Delete(config);
        Linux().Locate(instance).Requirements.Should().BeEmpty();

        File.WriteAllText(config, """{"runtimeOptions":{}}""");
        Linux().Locate(instance).Requirements.Should().BeEmpty();
    }
}
