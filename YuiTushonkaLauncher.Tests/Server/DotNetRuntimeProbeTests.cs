using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class DotNetRuntimeProbeTests
{
    [Fact]
    public void Parse_ReadsDotnetListRuntimesLines()
    {
        var parsed = DotNetRuntimeProbe.Parse([
            "Microsoft.AspNetCore.App 10.0.10 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]",
            "Microsoft.NETCore.App 9.0.21 [/usr/share/dotnet/shared/Microsoft.NETCore.App]",
            "Microsoft.NETCore.App 11.0.0-preview.1.25 [/opt/dotnet/shared/Microsoft.NETCore.App]",
        ]);

        parsed
            .Should()
            .Equal(
                new FrameworkRequirement("Microsoft.AspNetCore.App", "10.0.10"),
                new FrameworkRequirement("Microsoft.NETCore.App", "9.0.21"),
                new FrameworkRequirement("Microsoft.NETCore.App", "11.0.0-preview.1.25")
            );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Microsoft.NETCore.App")]
    [InlineData("Microsoft.NETCore.App notaversion [/x]")]
    [InlineData("It was not possible to find any installed .NET Core SDKs")]
    public void Parse_SkipsLinesThatAreNotFrameworks(string line)
    {
        DotNetRuntimeProbe.Parse([line]).Should().BeEmpty();
    }

    [Fact]
    public async Task ListInstalledAsync_AsksDotnet()
    {
        var probe = new DotNetRuntimeProbe(
            new FakeFinder(("dotnet", "/usr/bin/dotnet")),
            new ListingRunner("Microsoft.NETCore.App 10.0.10 [/x]")
        );

        var installed = await probe.ListInstalledAsync();

        installed.Should().Equal(new FrameworkRequirement("Microsoft.NETCore.App", "10.0.10"));
    }

    [Fact]
    public async Task ListInstalledAsync_IsNull_WhenDotnetCannotBeFound()
    {
        var probe = new DotNetRuntimeProbe(new FakeFinder(), new FakeRunner());

        (await probe.ListInstalledAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ListInstalledAsync_IsNull_WhenDotnetFailsOrTimesOut()
    {
        var failing = new FakeRunner { Behavior = _ => Task.FromResult(new ProcessRunResult(1)) };
        var timedOut = new FakeRunner { Behavior = _ => Task.FromResult(new ProcessRunResult(-1, TimedOut: true)) };
        var finder = new FakeFinder(("dotnet", "/usr/bin/dotnet"));

        (await new DotNetRuntimeProbe(finder, failing).ListInstalledAsync()).Should().BeNull();
        (await new DotNetRuntimeProbe(finder, timedOut).ListInstalledAsync()).Should().BeNull();
    }

    /// <summary>A runner that answers <c>dotnet --list-runtimes</c> with fixed lines.</summary>
    private sealed class ListingRunner(params string[] lines) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            LaunchCommand command,
            IProgress<ProcessLine>? output = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            command.Arguments.Should().Equal("--list-runtimes");

            foreach (var line in lines)
            {
                output?.Report(new ProcessLine(false, line));
            }

            return Task.FromResult(new ProcessRunResult(0));
        }
    }
}
