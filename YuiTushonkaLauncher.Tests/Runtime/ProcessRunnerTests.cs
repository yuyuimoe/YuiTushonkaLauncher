using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Runtime;

namespace YuiTushonkaLauncher.Tests.Runtime;

/// <summary>Runs real child processes through <c>/bin/sh</c>, so these are Linux-only.</summary>
public sealed class ProcessRunnerTests
{
    private static readonly ProcessRunner Runner = new(NullLogger<ProcessRunner>.Instance);

    private static LaunchCommand Shell(
        string script,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null
    )
    {
        return new LaunchCommand
        {
            FileName = "/bin/sh",
            Arguments = ["-c", script],
            Environment = environment ?? new Dictionary<string, string>(),
            WorkingDirectory = workingDirectory,
        };
    }

    private sealed class Collector : IProgress<ProcessLine>
    {
        private readonly object _gate = new();
        private readonly List<ProcessLine> _lines = [];

        public IReadOnlyList<ProcessLine> Lines
        {
            get
            {
                lock (_gate)
                {
                    return _lines.ToList();
                }
            }
        }

        public void Report(ProcessLine value)
        {
            lock (_gate)
            {
                _lines.Add(value);
            }
        }
    }

    [LinuxFact]
    public async Task RunAsync_TagsStdoutAndStderrLinesAndReturnsTheExitCode()
    {
        var output = new Collector();

        var result = await Runner.RunAsync(Shell("echo out1; echo err1 >&2; echo out2; exit 3"), output);

        result.ExitCode.Should().Be(3);
        result.TimedOut.Should().BeFalse();
        output.Lines.Where(line => !line.IsError).Select(line => line.Text).Should().Equal("out1", "out2");
        output.Lines.Where(line => line.IsError).Select(line => line.Text).Should().Equal("err1");
    }

    [LinuxFact]
    public async Task RunAsync_PassesVariablesAndTheWorkingDirectory()
    {
        var output = new Collector();
        using var folder = new Tests.Infrastructure.TempDirectory();

        await Runner.RunAsync(
            Shell("echo \"$YTT_TEST_VAR\"; pwd", new Dictionary<string, string> { ["YTT_TEST_VAR"] = "hello world" }, folder.Path),
            output
        );

        output.Lines.Select(line => line.Text).Should().Equal("hello world", Path.GetFullPath(folder.Path));
    }

    [LinuxFact]
    public async Task RunAsync_PassesArgumentsWithoutAShellSoSpacesAndQuotesSurvive()
    {
        var output = new Collector();
        var command = new LaunchCommand { FileName = "/bin/sh", Arguments = ["-c", "printf '%s|' \"$@\"", "sh", "a b", "it's", "\"q\""] };

        await Runner.RunAsync(command, output);

        output.Lines.Should().ContainSingle().Which.Text.Should().Be("a b|it's|\"q\"|");
    }

    [LinuxFact]
    public async Task RunAsync_ClosesStdinSoAToolThatWaitsForInputDoesNotHang()
    {
        var output = new Collector();
        var started = Stopwatch.StartNew();

        var result = await Runner.RunAsync(Shell("cat; echo done"), output, TimeSpan.FromSeconds(20));

        result.TimedOut.Should().BeFalse();
        output.Lines.Should().ContainSingle().Which.Text.Should().Be("done");
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [LinuxFact]
    public async Task RunAsync_StopsAProcessThatOutlivesItsTimeout()
    {
        var started = Stopwatch.StartNew();

        var result = await Runner.RunAsync(Shell("sleep 60"), timeout: TimeSpan.FromMilliseconds(500));

        result.TimedOut.Should().BeTrue();
        result.ExitCode.Should().Be(-1);
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [LinuxFact]
    public async Task RunAsync_KillsTheWholeTreeOnTimeout()
    {
        using var folder = new Tests.Infrastructure.TempDirectory();
        var pidFile = Path.Combine(folder.Path, "child.pid");

        // The shell starts a grandchild, as Wine does; killing only the parent would leave it running.
        var result = await Runner.RunAsync(Shell($"sleep 60 & echo $! > '{pidFile}'; wait"), timeout: TimeSpan.FromMilliseconds(800));

        result.TimedOut.Should().BeTrue();
        var childPid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
        await WaitUntilGoneAsync(childPid);
        Directory.Exists($"/proc/{childPid}").Should().BeFalse("the grandchild must not be orphaned");
    }

    [LinuxFact]
    public async Task RunAsync_CancellationStopsTheProcessAndThrows()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var action = () => Runner.RunAsync(Shell("sleep 60"), cancellationToken: source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [LinuxFact]
    public async Task RunAsync_DoesNotHangOnADaemonThatKeepsThePipesOpen()
    {
        var started = Stopwatch.StartNew();

        // wineserver does exactly this: the parent exits, a background process holds stdout.
        var result = await Runner.RunAsync(Shell("sleep 5 & echo started"), timeout: TimeSpan.FromSeconds(20));

        result.ExitCode.Should().Be(0);
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [LinuxFact]
    public async Task RunAsync_AProgramThatDoesNotExistThrowsInsteadOfReturningACode()
    {
        var action = () => Runner.RunAsync(new LaunchCommand { FileName = "/no/such/program" });

        await action.Should().ThrowAsync<System.ComponentModel.Win32Exception>();
    }

    private static async Task WaitUntilGoneAsync(int pid)
    {
        for (var attempt = 0; attempt < 40 && Directory.Exists($"/proc/{pid}"); attempt++)
        {
            await Task.Delay(100);
        }
    }
}
