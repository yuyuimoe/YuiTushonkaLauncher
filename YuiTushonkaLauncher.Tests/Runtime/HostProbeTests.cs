using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Runtime;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Runtime;

/// <summary>The thin adapters over the real machine: <c>PATH</c>, the process list and the platform.</summary>
public sealed class HostProbeTests
{
    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void ExecutableFinder_FindsAnExecutableFileOnTheGivenPath()
    {
        using var folder = new TempDirectory();
        var program = Path.Combine(folder.Path, "umu-run");
        File.WriteAllText(program, "#!/bin/sh\n");
        File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        new ExecutableFinder(folder.Path).Find("umu-run").Should().Be(program);
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void ExecutableFinder_IgnoresAFileWithoutTheExecuteBit()
    {
        using var folder = new TempDirectory();
        var file = Path.Combine(folder.Path, "wine");
        File.WriteAllText(file, "not a program");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        new ExecutableFinder(folder.Path).Find("wine").Should().BeNull();
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void ExecutableFinder_TakesTheFirstMatchInPathOrderAndSkipsBlankEntries()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        foreach (var folder in new[] { first, second })
        {
            var program = Path.Combine(folder.Path, "winetricks");
            File.WriteAllText(program, "x");
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        var path = string.Join(Path.PathSeparator, "", Path.Combine(first.Path, "missing"), first.Path, second.Path);

        new ExecutableFinder(path).Find("winetricks").Should().Be(Path.Combine(first.Path, "winetricks"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/usr/bin/wine")]
    public void ExecutableFinder_RefusesAnEmptyNameAndAnyNameThatIsAlreadyAPath(string name)
    {
        new ExecutableFinder("/usr/bin").Find(name).Should().BeNull();
    }

    [Fact]
    public void ExecutableFinder_OnAnEmptyPathFindsNothing()
    {
        new ExecutableFinder("").Find("wine").Should().BeNull();
    }

    [LinuxFact]
    public void ExecutableFinder_WithoutAnOverrideReadsTheProcessPath()
    {
        new ExecutableFinder().Find("sh").Should().NotBeNull("every Linux machine has sh on PATH");
    }

    [LinuxFact]
    public void ProcessNames_ListsTheRunningTestProcess()
    {
        var own = File.ReadAllText("/proc/self/comm").Trim();

        new ProcessNames().Running().Should().Contain(own);
    }

    [Fact]
    public void PlatformInfo_ReportsExactlyTheCurrentOperatingSystem()
    {
        var platform = new PlatformInfo();

        platform.IsLinux.Should().Be(OperatingSystem.IsLinux());
        platform.IsWindows.Should().Be(OperatingSystem.IsWindows());
        (platform.IsLinux && platform.IsWindows).Should().BeFalse();
    }
}
