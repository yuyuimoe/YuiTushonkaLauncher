using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Patching;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Patching;

/// <summary>
/// Backups are the only way back, so finding all of them (and never following a link out of the
/// instance) is what makes restore trustworthy.
/// </summary>
public sealed class BackupScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly BackupScanner _scanner = new();

    private string Root
    {
        get { return _temp.Combine("instance"); }
    }

    [Fact]
    public void Discover_FindsNestedBackupsSortedByPath()
    {
        Write("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak");
        Write("spt/SPT_Runtime/BepInEx/core/lower.dll.spt-bak");

        var pairs = _scanner.Discover(Root);

        pairs.Should().HaveCount(2);
        pairs[0].RelativePath.Should().Be("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
        pairs[1].RelativePath.Should().Be("spt/SPT_Runtime/BepInEx/core/lower.dll");
        pairs[0].TargetPath.Should().Be(Path.Combine(Root, pairs[0].RelativePath));
    }

    [Fact]
    public void Discover_WithoutADirectory_ReturnsNothing()
    {
        _scanner.Discover(_temp.Combine("missing")).Should().BeEmpty();
    }

    [Fact]
    public void Discover_IgnoresFilesThatAreNotBackups()
    {
        Write("gamefiles/Assembly-CSharp.dll");
        Write("gamefiles/Assembly-CSharp.dll.spt-bak");

        _scanner.Discover(Root).Should().ContainSingle();
    }

    [Fact]
    public void Discover_DoesNotFollowSymlinkedDirectories()
    {
        var outside = _temp.Combine("outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "elsewhere.dll.spt-bak"), [1]);
        Directory.CreateDirectory(Root);

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Root, "link"), outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        _scanner.Discover(Root).Should().BeEmpty("a link must not make the walk escape the instance");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private void Write(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
    }
}

/// <summary>Hashes and the rename-based replace, which is what keeps patching away from hard links.</summary>
public sealed class FileHashAndAtomicWriteTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task ComputeAsync_MatchesTheKnownDigest()
    {
        var path = _temp.Combine("file.bin");
        await File.WriteAllTextAsync(path, "abc");

        var hash = await FileHash.ComputeAsync(path);

        hash.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void TryCompute_ForAMissingFile_ReturnsNull()
    {
        FileHash.TryCompute(_temp.Combine("missing.bin")).Should().BeNull();
    }

    [Fact]
    public async Task CommitAsync_ReplacesTheDestinationWithTheTemporary()
    {
        var temporary = _temp.Combine("new.tmp");
        var target = _temp.Combine("target.bin");
        await File.WriteAllTextAsync(temporary, "patched");
        await File.WriteAllTextAsync(target, "original");

        var hash = await AtomicWrite.CommitAsync(temporary, target);

        (await File.ReadAllTextAsync(target)).Should().Be("patched");
        File.Exists(temporary).Should().BeFalse();
        hash.Should().Be(await FileHash.ComputeAsync(target));
    }

    [Fact]
    public async Task CommitAsync_LeavesAHardLinkOfTheOldContentIntact()
    {
        var target = _temp.Combine("target.bin");
        var link = _temp.Combine("link.bin");
        await File.WriteAllTextAsync(target, "original");
        await File.WriteAllTextAsync(_temp.Combine("new.tmp"), "patched");

        new FileLinkService(NullLogger<FileLinkService>.Instance).CreateHardLink(link, target);
        File.Exists(link).Should().BeTrue();

        await AtomicWrite.CommitAsync(_temp.Combine("new.tmp"), target);

        // The rename gives the target a new inode; the other link keeps the original bytes, which is
        // exactly what must not happen to the base install.
        (await File.ReadAllTextAsync(target))
            .Should()
            .Be("patched");
        (await File.ReadAllTextAsync(link)).Should().Be("original");
    }

    [Fact]
    public void Discard_RemovesATemporaryFileAndToleratesAMissingOne()
    {
        var path = _temp.Combine("temp.bin");
        File.WriteAllBytes(path, [1]);

        AtomicWrite.Discard(path);
        AtomicWrite.Discard(path);

        File.Exists(path).Should().BeFalse();
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
