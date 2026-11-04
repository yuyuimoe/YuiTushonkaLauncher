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

        // A shared machine recycles pids fast: if our program goes away, the pid can belong to a dotnet by the time it is
        // read. The attempt is repeated with a fresh program rather than asserting on a recycled number.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var process = StartSleep();
            var identity = await FindAsync(process);

            if (identity?.ExecutablePath is not null && identity.ExecutablePath.Contains("ytt-probe-", StringComparison.Ordinal))
            {
                identity.ProcessId.Should().Be(process.Id);
                identity.StartedAt.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeBefore(DateTimeOffset.UtcNow.AddSeconds(2));
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("The inspector never recognised the probe process.");
    }

    private async Task<ProcessIdentity?> FindAsync(Process process)
    {
        ProcessIdentity? identity = null;

        for (var attempt = 0; attempt < 100 && identity?.ExecutablePath is null && !process.HasExited; attempt++)
        {
            identity = _inspector.Find(process.Id);
            await Task.Delay(20);
        }

        return identity;
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
