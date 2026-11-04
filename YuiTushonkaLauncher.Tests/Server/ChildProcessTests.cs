using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Runtime;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ChildProcessTests
{
    private static ChildProcessSpawner Spawner(FakePlatform? platform = null, params (string, string)[] programs)
    {
        return new ChildProcessSpawner(
            platform ?? new FakePlatform(linux: true),
            new FakeFinder(programs),
            new NoSystemdRunner(),
            NullLogger<ChildProcessSpawner>.Instance
        );
    }

    /// <summary>Answers "is a systemd user manager running?" with no, so the spawner falls back to setsid.</summary>
    private sealed class NoSystemdRunner : YuiTushonkaLauncher.Core.Runtime.IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            LaunchCommand command,
            IProgress<ProcessLine>? output = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new ProcessRunResult(1));
        }
    }

    private static ChildProcessStart Shell(
        string script,
        Action<ProcessLine>? output = null,
        Dictionary<string, string>? environment = null,
        string? directory = null
    )
    {
        return new ChildProcessStart
        {
            Command = new LaunchCommand
            {
                FileName = "/bin/sh",
                Arguments = ["-c", script],
                Environment = environment ?? [],
                WorkingDirectory = directory,
            },
            OnOutput = output,
        };
    }

    private static async Task<int> ExitAsync(IChildProcess child)
    {
        return await child.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    // ---- what "tied to the launcher" means on this machine ----

    [Fact]
    public void Mechanism_IsTheStrongestTheMachineOffers()
    {
        using var scope = new ChildProcessSpawner(
            new FakePlatform(linux: true),
            new FakeFinder(("systemd-run", "/usr/bin/systemd-run"), ("setsid", "/usr/bin/setsid")),
            new SystemdRunner(available: true),
            NullLogger<ChildProcessSpawner>.Instance
        );
        using var session = Spawner(new FakePlatform(linux: true), ("setsid", "/usr/bin/setsid"));
        using var bare = Spawner(new FakePlatform(linux: true));
        using var windows = Spawner(new FakePlatform(linux: false));

        using var noUserManager = new ChildProcessSpawner(
            new FakePlatform(linux: true),
            new FakeFinder(("systemd-run", "/usr/bin/systemd-run"), ("setsid", "/usr/bin/setsid")),
            new SystemdRunner(available: false),
            NullLogger<ChildProcessSpawner>.Instance
        );

        scope.Mechanism.Should().Be(RunMechanism.SystemdScope, "a scope has its own cgroup");
        noUserManager
            .Mechanism.Should()
            .Be(RunMechanism.Setsid, "systemd-run is installed but no user manager answers, so a session is all that is left");
        session.Mechanism.Should().Be(RunMechanism.Setsid);
        bare.Mechanism.Should().Be(RunMechanism.None, "nothing can detach here, and the plan says so");
        windows.Mechanism.Should().Be(RunMechanism.WindowsJob);
    }

    /// <summary>Says whether a systemd user manager answers.</summary>
    private sealed class SystemdRunner(bool available) : YuiTushonkaLauncher.Core.Runtime.IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            LaunchCommand command,
            IProgress<ProcessLine>? output = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new ProcessRunResult(available ? 0 : 1));
        }
    }

    [LinuxFact]
    public async Task Start_PutsTheChildInItsOwnSession_SoTheLauncherCannotTakeItDown()
    {
        var setsid = new ExecutableLookup().Find("setsid");

        if (setsid is null)
        {
            return;
        }

        using var spawner = Spawner(new FakePlatform(linux: true), ("setsid", setsid));
        var child = spawner.Start(Shell("exec sleep 60"));

        try
        {
            await Task.Delay(200);

            var session = SessionOf(child.ProcessId);
            session.Should().NotBeNull();
            session.Should().NotBe(SessionOf(Environment.ProcessId), "a new session is what makes the child outlive the launcher");
            spawner.Mechanism.Should().Be(RunMechanism.Setsid);
        }
        finally
        {
            child.Kill();
            await ExitAsync(child);
        }
    }

    // ---- real processes ----

    [LinuxFact]
    public async Task Start_StreamsStdoutAndStderr_AndReportsTheExitCode()
    {
        using var spawner = Spawner();
        var lines = new List<ProcessLine>();

        var child = spawner.Start(
            Shell(
                "echo out; echo err 1>&2; exit 3",
                line =>
                {
                    lock (lines)
                    {
                        lines.Add(line);
                    }
                }
            )
        );
        var code = await ExitAsync(child);

        code.Should().Be(3);
        child.HasExited.Should().BeTrue();
        lines.Should().Contain(new ProcessLine(false, "out")).And.Contain(new ProcessLine(true, "err"));
    }

    [LinuxFact]
    public async Task Start_ClosesStdin_SoAServerThatWaitsForAKeyDoesNotHang()
    {
        // The real server prints "Press any key to exit..." after a fatal error and then waits.
        using var spawner = Spawner();
        var lines = new List<string>();

        var child = spawner.Start(
            Shell(
                "read key; echo got:[$key]",
                line =>
                {
                    lock (lines)
                    {
                        lines.Add(line.Text);
                    }
                }
            )
        );
        await ExitAsync(child);

        lines.Should().Contain("got:[]");
    }

    [LinuxFact]
    public async Task Start_PassesTheWorkingDirectoryAndTheEnvironment()
    {
        using var spawner = Spawner();
        using var temp = new Infrastructure.TempDirectory();
        var lines = new List<string>();

        var child = spawner.Start(
            Shell(
                "pwd; echo $YTT_PROBE",
                line =>
                {
                    lock (lines)
                    {
                        lines.Add(line.Text);
                    }
                },
                new() { ["YTT_PROBE"] = "hello" },
                temp.Path
            )
        );
        await ExitAsync(child);

        lines.Should().Equal(Path.GetFullPath(temp.Path), "hello");
    }

    [LinuxFact]
    public async Task RequestStop_SendsSigterm_SoAServerCanShutDownCleanly()
    {
        using var spawner = Spawner();
        var child = spawner.Start(Shell("exec sleep 60"));

        child.RequestStop();

        (await ExitAsync(child)).Should().Be(143, "128 plus SIGTERM, the signal the real server handles with 'Stopping server...'");
    }

    [LinuxFact]
    public async Task Kill_EndsAProcessThatIgnoresSigterm_AndEverythingItStarted()
    {
        using var spawner = Spawner();
        var ready = new TaskCompletionSource();
        var child = spawner.Start(Shell("trap '' TERM; echo ready; sleep 60 & wait", line => ready.TrySetResult()));

        // Only after the shell has installed its trap: a SIGTERM sent earlier would still kill it.
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        child.RequestStop();
        await Task.Delay(300);
        child.HasExited.Should().BeFalse("it ignores the polite request");

        child.Kill();

        (await ExitAsync(child)).Should().Be(137);
        Process
            .GetProcesses()
            .Count(process => process.ProcessName == "sleep" && SafeParent(process) == child.ProcessId)
            .Should()
            .Be(0, "the whole tree goes");
    }

    [LinuxFact]
    public async Task RequestStop_AndKill_AreHarmless_OnAProcessThatAlreadyEnded()
    {
        using var spawner = Spawner();
        var child = spawner.Start(Shell("exit 0"));
        await ExitAsync(child);

        child.RequestStop();
        child.Kill();

        child.HasExited.Should().BeTrue();
    }

    [LinuxFact]
    public async Task Start_ReportsTheStartTime_ThatTheLauncherRecordsToRecogniseItLater()
    {
        using var spawner = Spawner();
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);

        var child = spawner.Start(Shell("exec sleep 60"));

        child.StartedAt.Should().BeAfter(before).And.BeBefore(DateTimeOffset.UtcNow.AddSeconds(2));
        child.ProcessId.Should().BePositive();

        child.Kill();
        await ExitAsync(child);
    }

    [LinuxFact]
    public async Task Start_SurvivesAProgramThatExitsImmediately_AndStillReportsItsExitCode()
    {
        // Regression: Process.StartTime throws once the process is gone, which a server that fails on its first line does
        // at once. The child must still exist and report the code, or the real reason for the failure would be lost.
        using var spawner = Spawner();

        var child = spawner.Start(Shell("exit 9"));

        (await ExitAsync(child)).Should().Be(9);
        child.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30));
    }

    [LinuxFact]
    public void Start_ThrowsForAProgramThatDoesNotExist()
    {
        using var spawner = Spawner();

        var act = () => spawner.Start(new ChildProcessStart { Command = new LaunchCommand { FileName = "/nonexistent/server" } });

        act.Should().Throw<System.ComponentModel.Win32Exception>();
    }

    [LinuxFact]
    public async Task Start_CreatesEveryChildOnTheDedicatedThread_EvenWhenCalledFromAPoolThread()
    {
        using var spawner = Spawner();

        var child = await Task.Run(() => spawner.Start(Shell("exit 0")));
        await ExitAsync(child);

        spawner.LastSpawnerThread.Should().NotBeNull();
        spawner
            .LastSpawnerThread!.IsThreadPoolThread.Should()
            .BeFalse("a child created on a pool thread gets its death signal when the pool retires that thread");
        spawner.LastSpawnerThread.Name.Should().Be("child-process-spawner");
    }

    // ---- the thread every child is started from ----

    [Fact]
    public async Task SpawnThread_RunsEveryJobOnOneThreadThatIsNotAPoolThread()
    {
        // Linux ties a child's parent-death signal to the thread that created it. A pool thread is retired after about
        // twenty seconds, which would stop the server with the launcher still running. One thread that never ends is safe.
        using var thread = new SpawnThread();

        var ids = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(_ =>
                    Task.Run(() => thread.RunAsync(() => (Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread)))
                )
        );

        ids.Select(item => item.CurrentManagedThreadId).Distinct().Should().ContainSingle();
        ids.Should().OnlyContain(item => !item.IsThreadPoolThread);
        ids[0].CurrentManagedThreadId.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task SpawnThread_PassesAnExceptionToTheCaller_AndKeepsWorking()
    {
        using var thread = new SpawnThread();

        await FluentActions
            .Awaiting(() => thread.RunAsync<int>(() => throw new InvalidOperationException("no")))
            .Should()
            .ThrowAsync<InvalidOperationException>();

        (await thread.RunAsync(() => 7)).Should().Be(7);
    }

    /// <summary>The session id of a process, from <c>/proc/&lt;pid&gt;/stat</c> (field 6).</summary>
    private static int? SessionOf(int processId)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{processId}/stat");
            return int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[3], System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is IOException or FormatException)
        {
            return null;
        }
    }

    private static int SafeParent(Process process)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{process.Id}/stat");
            return int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1]);
        }
        catch (Exception exception) when (exception is IOException or FormatException)
        {
            return -1;
        }
    }

    private sealed class ExecutableLookup
    {
        public string? Find(string name)
        {
            return new ExecutableFinder().Find(name);
        }
    }
}
