using System.ComponentModel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Runtime;

namespace YuiTushonkaLauncher.Tests.Runtime;

public sealed class PrefixServiceTests
{
    private static PrefixService CreateService(
        RuntimeWorld world,
        FakeRunner runner,
        IExecutableFinder? finder = null,
        TimeSpan? settle = null
    )
    {
        return new PrefixService(
            world.CreateLauncher(finder: finder),
            runner,
            NullLogger<PrefixService>.Instance,
            settle ?? TimeSpan.FromMilliseconds(300)
        );
    }

    /// <summary>Does what wineboot does: writes the registry files in the prefix named by the command.</summary>
    private static Task<ProcessRunResult> WriteRegistry(LaunchCommand command)
    {
        var prefix = command.Environment["WINEPREFIX"];
        Directory.CreateDirectory(prefix);
        File.WriteAllText(Path.Combine(prefix, "system.reg"), "REG");
        File.WriteAllText(Path.Combine(prefix, "user.reg"), "REG");
        return Task.FromResult(new ProcessRunResult(0));
    }

    [Fact]
    public async Task InitializeAsync_CreatesAFreshPrefixThroughWinebootAndReportsItInitialized()
    {
        using var world = new RuntimeWorld();
        var runner = new FakeRunner { Behavior = WriteRegistry };
        var profile = world.ProtonProfile(world.CreateProton("GE-Proton11-1"));
        var lines = new List<ProcessLine>();

        var result = await CreateService(world, runner).InitializeAsync(world.Instance, profile, new SynchronousProgress(lines.Add));

        result.Succeeded.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Prefix.State.Should().Be(PrefixState.Initialized);
        result.Prefix.Path.Should().Be(Path.Combine(world.InstanceRoot, "wineprefix"));
        runner.Commands.Should().ContainSingle().Which.Arguments.Should().Equal("wineboot", "-u");
        lines.Should().ContainSingle().Which.Text.Should().Be("fake output");
    }

    [Fact]
    public async Task InitializeAsync_WaitsForTheRegistryThatWineWritesAfterWinebootExits()
    {
        using var world = new RuntimeWorld();
        var runner = new FakeRunner();
        runner.Behavior = command =>
        {
            // wineboot has exited; the server flushes the registry a moment later.
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                await WriteRegistry(command);
            });
            return Task.FromResult(new ProcessRunResult(0));
        };

        var result = await CreateService(world, runner, settle: TimeSpan.FromSeconds(10))
            .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1")));

        result.Succeeded.Should().BeTrue();
        result.Prefix.State.Should().Be(PrefixState.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_FailsWhenTheRegistryNeverAppears()
    {
        using var world = new RuntimeWorld();
        var runner = new FakeRunner();

        var result = await CreateService(world, runner, settle: TimeSpan.FromMilliseconds(300))
            .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1")));

        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.Prefix.State.Should().NotBe(PrefixState.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_ReportsAFailingExitCodeAndDoesNotWaitForTheRegistry()
    {
        using var world = new RuntimeWorld();
        var runner = new FakeRunner { Behavior = _ => Task.FromResult(new ProcessRunResult(3)) };

        var started = DateTime.UtcNow;
        var result = await CreateService(world, runner, settle: TimeSpan.FromSeconds(30))
            .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1")));

        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(3);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task InitializeAsync_ATimeoutIsAFailureWithNoExitCode()
    {
        using var world = new RuntimeWorld();
        var runner = new FakeRunner { Behavior = _ => Task.FromResult(new ProcessRunResult(-1, TimedOut: true)) };

        var result = await CreateService(world, runner)
            .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1")));

        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_RefusesBeforeStartingAnythingWhenPreflightFails()
    {
        using var world = new RuntimeWorld();
        var runner = new FakeRunner();

        var result = await CreateService(world, runner).InitializeAsync(world.Instance, world.ProtonProfile(world.Combine("no-proton")));

        result.Succeeded.Should().BeFalse();
        result.Command.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.ProtonMissing);
        runner.Commands.Should().BeEmpty();
        Directory.Exists(Path.Combine(world.InstanceRoot, "wineprefix")).Should().BeFalse("nothing is created when pre-flight fails");
    }

    [Fact]
    public async Task InitializeAsync_NeverTouchesAFolderThatIsNotAPrefix()
    {
        using var world = new RuntimeWorld();
        var foreign = world.Combine("foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "notes.txt"), "precious");
        var runner = new FakeRunner();

        var result = await CreateService(world, runner)
            .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1"), row => row.PrefixPath = foreign));

        result.Succeeded.Should().BeFalse();
        result.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.PrefixBroken);
        runner.Commands.Should().BeEmpty();
        Directory.GetFileSystemEntries(foreign).Should().ContainSingle().Which.Should().EndWith("notes.txt");
    }

    [Fact]
    public async Task InitializeAsync_RepairsAPartialPrefixInPlace()
    {
        using var world = new RuntimeWorld();
        var prefix = world.CreatePrefix("partial");
        File.Delete(Path.Combine(prefix, "user.reg"));
        File.WriteAllText(Path.Combine(prefix, "keep-me.txt"), "mine");
        var runner = new FakeRunner { Behavior = WriteRegistry };

        var result = await CreateService(world, runner)
            .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1"), row => row.PrefixPath = prefix));

        result.Succeeded.Should().BeTrue();
        File.Exists(Path.Combine(prefix, "keep-me.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_AToolThatCannotStartBecomesAProblemNotACrash()
    {
        using var world = new RuntimeWorld();
        var runner = new FakeRunner { Behavior = _ => throw new Win32Exception("No such file or directory") };

        var result = await CreateService(world, runner)
            .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1")));

        result.Succeeded.Should().BeFalse();
        result.Problems.Should().ContainSingle().Which.Code.Should().Be(RuntimeProblemCode.ToolMissing);
    }

    [Fact]
    public async Task InitializeAsync_PropagatesCancellation()
    {
        using var world = new RuntimeWorld();
        using var source = new CancellationTokenSource();
        var runner = new FakeRunner { Behavior = _ => throw new OperationCanceledException() };

        var action = () =>
            CreateService(world, runner)
                .InitializeAsync(world.Instance, world.ProtonProfile(world.CreateProton("GE-Proton11-1")), cancellationToken: source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Inspect_DelegatesToTheInspector()
    {
        using var world = new RuntimeWorld();

        CreateService(world, new FakeRunner()).Inspect(world.CreatePrefix()).State.Should().Be(PrefixState.Initialized);
    }

    private sealed class SynchronousProgress(Action<ProcessLine> handler) : IProgress<ProcessLine>
    {
        public void Report(ProcessLine value)
        {
            handler(value);
        }
    }
}
