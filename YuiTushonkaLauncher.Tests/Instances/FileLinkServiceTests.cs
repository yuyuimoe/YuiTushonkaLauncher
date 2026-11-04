using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

public sealed class FileLinkServiceTests
{
    private readonly TempDirectory _temp = new();
    private readonly FileLinkService _links;

    public FileLinkServiceTests()
    {
        _links = new FileLinkService(NullLogger<FileLinkService>.Instance);
    }

    [Fact]
    public void CreateHardLink_SharesTheContentAndIncreasesTheLinkCount()
    {
        var source = _temp.Combine("base.dll");
        var link = _temp.Combine("instance.dll");
        File.WriteAllText(source, "payload");

        _links.CreateHardLink(link, source);

        _links.GetLinkCount(source).Should().Be(2);
        _links.GetLinkCount(link).Should().Be(2);
        File.ReadAllText(link).Should().Be("payload");
    }

    [Fact]
    public void GetLinkCount_ReturnsOneForAPrivateFile()
    {
        var path = _temp.Combine("private.txt");
        File.WriteAllText(path, "x");

        _links.GetLinkCount(path).Should().Be(1);
    }

    [Fact]
    public void Detach_ReplacesASharedFileWithAPrivateCopy()
    {
        var source = _temp.Combine("base.dll");
        var link = _temp.Combine("instance.dll");
        File.WriteAllText(source, "payload");
        _links.CreateHardLink(link, source);

        _links.Detach(link);

        _links.GetLinkCount(link).Should().Be(1, "the copy-on-write step must break the link");
        _links.GetLinkCount(source).Should().Be(1);
        File.ReadAllText(link).Should().Be("payload");
    }

    [Fact]
    public void Detach_LeavesTheBaseFileUntouchedAfterAnEdit()
    {
        var source = _temp.Combine("base.cfg");
        var link = _temp.Combine("instance.cfg");
        File.WriteAllText(source, "original");
        _links.CreateHardLink(link, source);

        _links.Detach(link);
        File.WriteAllText(link, "edited");

        File.ReadAllText(source).Should().Be("original");
        File.ReadAllText(link).Should().Be("edited");
    }

    [Fact]
    public void Detach_IsANoOpForPrivateOrMissingFiles()
    {
        var path = _temp.Combine("private.txt");
        File.WriteAllText(path, "x");
        var before = File.GetLastWriteTimeUtc(path);

        _links.Detach(path);
        _links.Detach(_temp.Combine("missing.txt"));

        File.GetLastWriteTimeUtc(path).Should().Be(before);
        File.Exists(_temp.Combine("missing.txt")).Should().BeFalse();
    }

    [Fact]
    public void IsSameVolume_IsTrueForPathsOnTheSameFileSystem()
    {
        Directory.CreateDirectory(_temp.Combine("a"));
        Directory.CreateDirectory(_temp.Combine("b"));

        _links.IsSameVolume(_temp.Combine("a"), _temp.Combine("b")).Should().BeTrue();
    }

    [Fact]
    public void GetAvailableFreeSpace_ReturnsAValue()
    {
        _links.GetAvailableFreeSpace(_temp.Path).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CopyFile_CopiesContentAndCreatesTheParentDirectory()
    {
        var source = _temp.Combine("source.txt");
        File.WriteAllText(source, "copy me");

        _links.CopyFile(source, _temp.Combine("nested", "destination.txt"), overwrite: true);

        File.ReadAllText(_temp.Combine("nested", "destination.txt")).Should().Be("copy me");
        _links.GetLinkCount(_temp.Combine("nested", "destination.txt")).Should().Be(1);
    }

    [Fact]
    public async Task CopyDirectoryAsync_CopiesTheWholeTree()
    {
        Directory.CreateDirectory(_temp.Combine("source", "nested"));
        await File.WriteAllTextAsync(_temp.Combine("source", "a.txt"), "a");
        await File.WriteAllTextAsync(_temp.Combine("source", "nested", "b.txt"), "b");

        await _links.CopyDirectoryAsync(_temp.Combine("source"), _temp.Combine("destination"));

        File.ReadAllText(_temp.Combine("destination", "a.txt")).Should().Be("a");
        File.ReadAllText(_temp.Combine("destination", "nested", "b.txt")).Should().Be("b");
    }

    [Fact]
    public void CreateSymbolicLink_CreatesAFileLink()
    {
        var source = _temp.Combine("target.txt");
        File.WriteAllText(source, "sym");

        _links.CreateSymbolicLink(_temp.Combine("link.txt"), source, isDirectory: false);

        new FileInfo(_temp.Combine("link.txt")).LinkTarget.Should().NotBeNull();
        File.ReadAllText(_temp.Combine("link.txt")).Should().Be("sym");
    }

    [Fact]
    public void CreateHardLink_OnAMissingSource_ThrowsAMappedError()
    {
        var act = () => _links.CreateHardLink(_temp.Combine("link.dll"), _temp.Combine("missing.dll"));

        act.Should()
            .Throw<FileLinkException>()
            .Which.Error.Should()
            .BeOneOf(FileLinkError.NotFound, FileLinkError.Unknown, FileLinkError.PermissionDenied);
    }
}
