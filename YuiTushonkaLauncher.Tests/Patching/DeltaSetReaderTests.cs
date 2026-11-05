using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Patching;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Patching;

/// <summary>
/// The reader turns a directory of <c>.delta</c> files into instance-relative targets; the real SPT
/// tree nests the delta under the game-root path, so a reader that only looks one level deep finds
/// nothing on a real install.
/// </summary>
public sealed class DeltaSetReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakePatchEngine _engine = new();
    private readonly DeltaSetReader _reader;

    public DeltaSetReaderTests()
    {
        _reader = new DeltaSetReader(_engine, NullLogger<DeltaSetReader>.Instance);
    }

    private string Patches
    {
        get { return _temp.Combine("instance", "SPT_Runtime", "SPT_Data", "Launcher", "Patches"); }
    }

    [Fact]
    public void Read_WithoutAPatchesDirectory_ReturnsNothing()
    {
        var result = _reader.Read(_temp.Combine("instance"), Patches);

        result.Deltas.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Read_ANestedDelta_ResolvesToThePathAtTheInstanceRoot()
    {
        WriteDelta("SPT-core/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");

        var result = _reader.Read(_temp.Combine("instance"), Patches);

        var delta = result.Deltas.Should().ContainSingle().Subject;
        delta.RelativePath.Should().Be("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
        delta.BackupRelativePath.Should().Be("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak");
        delta.ExpectedInputSize.Should().Be(1000);
        delta.ExpectedOutputSize.Should().Be(1012);
    }

    [Fact]
    public void Read_ADeltaUnderTheRuntimeRoot_KeepsTheRuntimeDirectory()
    {
        WriteDelta("SPT-core/SPT_Runtime/whatever.dll");

        var result = _reader.Read(_temp.Combine("instance"), Patches);

        result.Deltas.Should().ContainSingle().Which.RelativePath.Should().Be("SPT_Runtime/whatever.dll");
    }

    [Fact]
    public void Read_AnUnreadableDelta_IsWarnedAboutAndSkipped()
    {
        Directory.CreateDirectory(Path.Combine(Patches, "SPT-core"));
        File.WriteAllText(Path.Combine(Patches, "SPT-core", "broken.delta"), "not a delta");

        var result = _reader.Read(_temp.Combine("instance"), Patches);

        result.Deltas.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("broken");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private void WriteDelta(string relativePath, long inputSize = 1000, long outputSize = 1012)
    {
        var path = Path.Combine(Patches, relativePath + ".delta");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.WriteAllText(path + FakePatchEngine.HeaderSuffix, $"{inputSize} {outputSize}");
    }
}
