using System.Diagnostics;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

/// <remarks>
/// Every test here starts its own copy of <c>sleep</c> under a unique name. A shared name would be a shared resource:
/// xunit runs classes in parallel, other tests kill their own <c>sleep</c>, and a recycled pid would then be read as
/// somebody else's process.
/// </remarks>
public sealed class ProcessInspectorTests : IDisposable
{
    private readonly ProcessInspector _inspector = new();

    private readonly TempDirectory _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
    }

    private static Process StartShell(string script)
    {
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        return Process.Start(info)!;
    }

    /// <summary>Starts a copy of <c>sleep</c> whose file name is unique, so nothing else in the suite can match it.</summary>
    private Process StartSleep()
    {
        var directory = _temp.Combine("probe");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, $"ytt-probe-{Guid.NewGuid():n}");

        File.Copy("/bin/sleep", executable);

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return Process.Start(new ProcessStartInfo(executable, "60") { UseShellExecute = false })!;
    }

    [LinuxFact]
    public async Task Find_DescribesARunningProcess_ByStartTimeAndExecutable()
    {
        var before = DateTimeOffset.UtcNow;
        using var process = StartSleep();

        try
        {
            // Reading another process's module list can fail for an instant under load; the inspector is allowed to answer
            // "cannot tell", so the test waits for the instant it can tell instead of asserting on the first attempt.
            ProcessIdentity? identity = null;

            for (var attempt = 0; attempt < 100 && identity?.ExecutablePath is null; attempt++)
            {
                identity = _inspector.Find(process.Id);
                await Task.Delay(20);
            }

            identity.Should().NotBeNull();
            identity!.ProcessId.Should().Be(process.Id);
            identity.ExecutablePath.Should().StartWith(_temp.Combine("probe")).And.Contain("ytt-probe-");
            identity.StartedAt.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeBefore(DateTimeOffset.UtcNow.AddSeconds(2));
        }
        finally
        {
            process.Kill();
        }
    }

    [Fact]
    public void Find_IsNull_ForAProcessThatDoesNotExist()
    {
        _inspector.Find(int.MaxValue - 7).Should().BeNull();
        _inspector.IsAlive(int.MaxValue - 7).Should().BeFalse();
    }

    [Fact]
    public void Find_SeesTheTestProcessItself()
    {
        _inspector.Find(Environment.ProcessId).Should().NotBeNull();
        _inspector.IsAlive(Environment.ProcessId).Should().BeTrue();
    }

    [LinuxFact]
    public async Task Stop_EndsAProcessThatHonoursSigterm_WithoutWaitingForTheGracePeriod()
    {
        using var process = StartSleep();
        var clock = Stopwatch.StartNew();

        var stopped = await _inspector.StopAsync(process.Id, TimeSpan.FromSeconds(20));

        stopped.Should().BeTrue();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(143);
    }

    [LinuxFact]
    public async Task Stop_KillsAProcessThatIgnoresSigterm_AfterTheGracePeriod()
    {
        using var process = StartShell("trap '' TERM; while true; do sleep 1; done");
        await Task.Delay(200);

        var stopped = await _inspector.StopAsync(process.Id, TimeSpan.FromMilliseconds(400));

        stopped.Should().BeTrue();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(137);
    }

    [Fact]
    public async Task Stop_IsTrue_ForAProcessThatIsAlreadyGone()
    {
        (await _inspector.StopAsync(int.MaxValue - 7, TimeSpan.FromSeconds(1))).Should().BeTrue();
    }
}
