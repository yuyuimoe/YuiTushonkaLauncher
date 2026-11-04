using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

public sealed class PatchStateDetectorTests
{
    private readonly PatchStateDetector _detector = new(NullLogger<PatchStateDetector>.Instance);

    [Fact]
    public void Detect_ReportsUnknownWhenTheBinaryIsMissing()
    {
        using var temp = new TempDirectory();

        var state = _detector.Detect(temp.Path);

        state.State.Should().Be(PatchState.Unknown);
        state.Reason.Should().Contain("not found");
    }

    [Fact]
    public void Detect_ReportsUnknownWithoutABackup()
    {
        using var temp = new TempDirectory();
        WriteTarget(temp, "patched");

        var state = _detector.Detect(temp.Path);

        state.State.Should().Be(PatchState.Unknown);
        state.TargetSha256.Should().NotBeNull();
        state.BackupPath.Should().BeNull();
    }

    [Fact]
    public void Detect_ReportsPatchedWhenTargetDiffersFromBackup()
    {
        using var temp = new TempDirectory();
        var target = WriteTarget(temp, "patched");
        File.WriteAllText(target + ".spt-bak", "original");

        var state = _detector.Detect(temp.Path);

        state.State.Should().Be(PatchState.Patched);
        state.TargetSha256.Should().NotBe(state.BackupSha256);
        state.BackupPath.Should().EndWith(".spt-bak");
    }

    [Fact]
    public void Detect_ReportsNotPatchedWhenTargetEqualsBackup()
    {
        using var temp = new TempDirectory();
        var target = WriteTarget(temp, "same-bytes");
        File.WriteAllText(target + ".spt-bak", "same-bytes");

        var state = _detector.Detect(temp.Path);

        state.State.Should().Be(PatchState.NotPatched);
        state.TargetSha256.Should().Be(state.BackupSha256);
    }

    private static string WriteTarget(TempDirectory temp, string content)
    {
        var path = Path.Combine(temp.Path, PatchStateDetector.RelativeTargetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
