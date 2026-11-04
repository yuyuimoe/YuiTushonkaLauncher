using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

public sealed class LinkAwareFileWriterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileLinkService _links;

    public LinkAwareFileWriterTests()
    {
        _links = new FileLinkService(NullLogger<FileLinkService>.Instance);
    }

    [Fact]
    public async Task WriteAllTextAsync_DetachesTheTargetBeforeWriting()
    {
        var baseFile = _temp.Combine("base.cfg");
        var instanceFile = _temp.Combine("instance.cfg");
        await File.WriteAllTextAsync(baseFile, "original");
        _links.CreateHardLink(instanceFile, baseFile);

        var writer = new LinkAwareFileWriter(_links);
        await writer.WriteAllTextAsync(instanceFile, "edited");

        (await File.ReadAllTextAsync(baseFile)).Should().Be("original");
        (await File.ReadAllTextAsync(instanceFile)).Should().Be("edited");
        _links.GetLinkCount(baseFile).Should().Be(1);
    }

    [Fact]
    public async Task WriteAllTextAsync_CreatesMissingDirectories()
    {
        var writer = new LinkAwareFileWriter(_links);

        await writer.WriteAllTextAsync(_temp.Combine("nested", "deeper", "file.json"), "{}");

        File.Exists(_temp.Combine("nested", "deeper", "file.json")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAllBytesAsync_DoesNotEmitABom()
    {
        var writer = new LinkAwareFileWriter(_links);

        await writer.WriteAllTextAsync(_temp.Combine("plain.txt"), "text");

        var bytes = await File.ReadAllBytesAsync(_temp.Combine("plain.txt"));
        bytes.Should().Equal("text"u8.ToArray());
    }

    [Fact]
    public async Task CopyFileAsync_OverwriteDetachesTheDestination()
    {
        var baseFile = _temp.Combine("base.bin");
        var instanceFile = _temp.Combine("instance.bin");
        await File.WriteAllTextAsync(baseFile, "base-bytes");
        _links.CreateHardLink(instanceFile, baseFile);

        var source = _temp.Combine("source.bin");
        await File.WriteAllTextAsync(source, "new-bytes");

        var writer = new LinkAwareFileWriter(_links);
        await writer.CopyFileAsync(source, instanceFile, overwrite: true);

        (await File.ReadAllTextAsync(instanceFile)).Should().Be("new-bytes");
        (await File.ReadAllTextAsync(baseFile)).Should().Be("base-bytes");
    }

    [Fact]
    public async Task CopyFileAsync_WithoutOverwrite_ThrowsWhenTheDestinationExists()
    {
        var source = _temp.Combine("source.bin");
        var destination = _temp.Combine("destination.bin");
        await File.WriteAllTextAsync(source, "a");
        await File.WriteAllTextAsync(destination, "b");

        var writer = new LinkAwareFileWriter(_links);
        var act = async () => await writer.CopyFileAsync(source, destination, overwrite: false);

        await act.Should().ThrowAsync<IOException>();
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
