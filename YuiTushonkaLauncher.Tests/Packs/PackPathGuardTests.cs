using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Infrastructure.Packs;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Packs;

/// <summary>The last line of defence before a config is written: where does this path really land?</summary>
public sealed class PackPathGuardTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string Root
    {
        get { return _temp.Combine("instance"); }
    }

    [Fact]
    public void Resolve_AnOrdinaryPath_LandsInsideTheInstance()
    {
        Directory.CreateDirectory(Root);

        var full = PackPathGuard.Resolve(Root, "spt/BepInEx/config/x.cfg");

        full.Should().Be(Path.Combine(Path.GetFullPath(Root), "spt", "BepInEx", "config", "x.cfg"));
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("a/../../outside.json")]
    [InlineData("/abs.json")]
    [InlineData("C:/abs.json")]
    [InlineData("a\\b.json")]
    public void Resolve_APathThatEscapes_IsRefused(string relative)
    {
        Directory.CreateDirectory(Root);

        var act = () => PackPathGuard.Resolve(Root, relative);

        act.Should().Throw<PackException>().Which.Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Fact]
    public void Resolve_ThroughASymlinkedDirectory_IsRefused()
    {
        var outside = _temp.Combine("outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Root);

        if (!TryLinkDirectory(Path.Combine(Root, "link"), outside))
        {
            return;
        }

        var act = () => PackPathGuard.Resolve(Root, "link/planted.json");

        act.Should().Throw<PackException>().WithMessage("*symbolic link*");
    }

    [Fact]
    public void Resolve_ToASymlinkedFile_IsRefused()
    {
        var outsideFile = _temp.Combine("outside.json");
        File.WriteAllText(outsideFile, "{}");
        Directory.CreateDirectory(Root);

        try
        {
            File.CreateSymbolicLink(Path.Combine(Root, "file.json"), outsideFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var act = () => PackPathGuard.Resolve(Root, "file.json");

        act.Should().Throw<PackException>().WithMessage("*symbolic link*");
    }

    [Fact]
    public void Resolve_ANewFileInANewDirectory_IsFine()
    {
        Directory.CreateDirectory(Root);

        var act = () => PackPathGuard.Resolve(Root, "does/not/exist/yet.json");

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private static bool TryLinkDirectory(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
