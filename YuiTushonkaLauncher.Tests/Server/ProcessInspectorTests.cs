using System.Diagnostics;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ProcessInspectorTests
{
    private readonly ProcessInspector _inspector = new();

    private static Process StartShell(string script)
    {
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        return Process.Start(info)!;
    }

    private static Process StartSleep()
    {
        return Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;
    }

    [LinuxFact]
    public void Find_DescribesARunningProcess_ByStartTimeAndExecutable()
    {
        using var process = StartSleep();

        try
        {
            var identity = _inspector.Find(process.Id);

            identity.Should().NotBeNull();
            identity!.ProcessId.Should().Be(process.Id);
            identity.ExecutablePath.Should().EndWith("sleep");
            identity
                .StartedAt.Should()
                .BeCloseTo(new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero), TimeSpan.FromSeconds(1));
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
