using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Infrastructure.Packs;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Packs;

public sealed class PackWriterTests : IDisposable
{
    private const string Owner = "com.fixture.clientmod";

    private readonly TempDirectory _temp = new();

    private static PackManifest Manifest()
    {
        return PackCrafter.Manifest(
            configs:
            [
                PackCrafter.Config(Owner, "spt/BepInEx/config/b.cfg", "b"),
                PackCrafter.Config(Owner, "spt/BepInEx/config/a.cfg", "a"),
            ]
        );
    }

    private static Dictionary<string, byte[]> Configs()
    {
        return new Dictionary<string, byte[]>
        {
            ["spt/BepInEx/config/b.cfg"] = Encoding.UTF8.GetBytes("b"),
            ["spt/BepInEx/config/a.cfg"] = Encoding.UTF8.GetBytes("a"),
        };
    }

    [Fact]
    public void Write_TheSameInputTwice_ProducesIdenticalBytes()
    {
        var first = _temp.Combine("first.sptpack");
        var second = _temp.Combine("second.sptpack");

        PackWriter.Write(first, Manifest(), Configs());
        PackWriter.Write(second, Manifest(), Configs());

        File.ReadAllBytes(first).Should().Equal(File.ReadAllBytes(second));
    }

    [Fact]
    public void Write_PutsTheManifestFirstThenTheReadmeThenConfigsInPathOrder()
    {
        var path = _temp.Combine("p.sptpack");

        PackWriter.Write(path, Manifest(), Configs());

        using var archive = ZipFile.OpenRead(path);
        archive
            .Entries.Select(entry => entry.FullName)
            .Should()
            .Equal("manifest.json", "README.txt", "configs/spt/BepInEx/config/a.cfg", "configs/spt/BepInEx/config/b.cfg");
        // A zip stores wall-clock time; compare that, not the offset the reader attaches.
        archive.Entries.Should().OnlyContain(entry => entry.LastWriteTime.DateTime == new DateTime(1980, 1, 1, 0, 0, 0));
    }

    [Fact]
    public void Write_TheReadmeNamesTheModsAndCarriesNoBinary()
    {
        var path = _temp.Combine("p.sptpack");

        PackWriter.Write(path, Manifest(), Configs());

        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("README.txt")!.Open());
        var readme = reader.ReadToEnd();

        readme.Should().Contain(Manifest().ShareCode).And.Contain(Owner).And.Contain("no mods");
        archive.Entries.Should().NotContain(entry => PackRules.IsForbiddenExtension(entry.FullName));
    }

    [Fact]
    public void Write_ReplacesAnExistingPackageAndLeavesNoPartFile()
    {
        var path = _temp.Combine("p.sptpack");
        File.WriteAllText(path, "old content");

        PackWriter.Write(path, Manifest(), Configs());

        File.Exists(path + ".part").Should().BeFalse();
        new FileInfo(path).Length.Should().BeGreaterThan(20);
        ZipFile.OpenRead(path).Dispose();
    }

    [Fact]
    public void Write_CreatesTheDestinationDirectory()
    {
        var path = _temp.Combine("deep", "er", "p.sptpack");

        PackWriter.Write(path, Manifest(), Configs());

        File.Exists(path).Should().BeTrue();
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
