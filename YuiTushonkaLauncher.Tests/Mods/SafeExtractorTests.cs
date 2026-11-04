using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Mods;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class SafeExtractorTests
{
    private readonly SafeExtractor _extractor = new(NullLogger<SafeExtractor>.Instance);
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task ExtractAsync_WritesEveryEntry()
    {
        using var archive = new ZipFixture().Add("BepInEx/plugins/x/mod.dll", "binary").Add("BepInEx/config/x.cfg", "[General]");

        var written = await _extractor.ExtractAsync(archive.Path, _temp.Combine("out"));

        written.Should().Be(2);
        File.ReadAllText(_temp.Combine("out", "BepInEx", "config", "x.cfg")).Should().Be("[General]");
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute/escape.txt")]
    [InlineData("BepInEx/../../escape.txt")]
    public async Task ExtractAsync_RejectsPathTraversal(string entryPath)
    {
        using var archive = new ZipFixture().Add(entryPath, "evil");
        var destination = _temp.Combine("safe");

        var act = async () => await _extractor.ExtractAsync(archive.Path, destination);

        await act.Should().ThrowAsync<FileLinkException>();
        File.Exists(_temp.Combine("escape.txt")).Should().BeFalse();
    }
}
