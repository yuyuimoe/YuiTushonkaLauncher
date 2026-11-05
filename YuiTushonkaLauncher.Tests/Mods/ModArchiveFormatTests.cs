using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Mods;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Mods;

/// <summary>
/// The Forge serves zip, 7z and rar, and the download URL ends in the release version — never in an extension — so a
/// name is not evidence. These tests pin that the format comes from the bytes, that a real 7z (plain and solid)
/// installs, and that a mislabelled file is read for what it is.
/// </summary>
public sealed class ModArchiveFormatTests
{
    [Fact]
    public void TheFormatIsReadFromTheFirstBytes()
    {
        using var temp = new TempDirectory();

        Detect(temp, "a.zip", [0x50, 0x4B, 0x03, 0x04]).Should().Be(ModArchiveFormat.Zip);
        Detect(temp, "a.7z", [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]).Should().Be(ModArchiveFormat.SevenZip);
        Detect(temp, "a.rar", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]).Should().Be(ModArchiveFormat.Rar);
        Detect(temp, "a.rar5", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]).Should().Be(ModArchiveFormat.Rar);
        Detect(temp, "a.bin", [0x00, 0x01, 0x02]).Should().Be(ModArchiveFormat.Unknown);

        // The extension is not evidence either way.
        Detect(temp, "a.zip", [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]).Should().Be(ModArchiveFormat.SevenZip);
    }

    [Fact]
    public void AnUnreadableFileIsRefusedByNameAndFormats()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("mystery.mod");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x04, 0x05]);

        var open = () => ModArchiveContent.Open(path);

        open.Should().Throw<ModArchiveException>().WithMessage("*mystery.mod*zip, 7z or rar*");
    }

    [Fact]
    public void AFileThatIsGoneIsNamedInTheError()
    {
        using var temp = new TempDirectory();

        var open = () => ModArchiveContent.Open(temp.Combine("gone.7z"));

        open.Should().Throw<ModArchiveException>().WithMessage("*gone.7z*not there*");
    }

    [Fact]
    public void ARealSevenZipIsInspectedLikeAZip()
    {
        var inspector = new ArchiveInspector(NullLogger<ArchiveInspector>.Instance);

        var inspection = inspector.Inspect(Fixture("mod.7z"));

        inspection.Kind.Should().Be(ModKind.Client);
        inspection.RootPrefix.Should().Be("ModRoot", "a wrapper folder is found in 7z exactly as in zip");
        inspection.CandidateAssemblies.Should().Contain("BepInEx/plugins/Fixture.Plugin.dll");
        inspection.Entries.Should().Contain(entry => entry.Kind == ArchiveEntryKind.Config);
        inspection.IsUnsafe.Should().BeFalse();
    }

    [Theory]
    [InlineData("mod.7z")]
    [InlineData("mod-solid.7z")]
    public async Task ARealSevenZipExtractsEveryFile(string fixture)
    {
        using var temp = new TempDirectory();
        var extractor = new SafeExtractor(NullLogger<SafeExtractor>.Instance);
        var destination = temp.Combine("staging");

        var written = await extractor.ExtractAsync(
            Path.Join(AppContext.BaseDirectory, "Mods", "Fixtures", fixture),
            destination,
            CancellationToken.None
        );

        written.Should().BeGreaterThanOrEqualTo(3);
        File.Exists(Path.Join(destination, "ModRoot", "BepInEx", "plugins", "Fixture.Plugin.dll")).Should().BeTrue();
        File.ReadAllText(Path.Join(destination, "ModRoot", "BepInEx", "config", "fixture.cfg"))
            .Should()
            .Contain("setting = true", "the content survives the decompression");
    }

    [Fact]
    public async Task AZipNamedLikeASevenZipIsStillRead()
    {
        using var zip = new ZipFixture().Add("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");
        using var temp = new TempDirectory();
        var mislabelled = temp.Combine("looks-like.7z");
        File.Copy(zip.Path, mislabelled);

        ModArchiveContent.Detect(mislabelled).Should().Be(ModArchiveFormat.Zip);
        var written = await new SafeExtractor(NullLogger<SafeExtractor>.Instance).ExtractAsync(
            mislabelled,
            temp.Combine("staging"),
            CancellationToken.None
        );

        written.Should().Be(1);
    }

    [Fact]
    public async Task ATraversalEntryInAZipIsStillRejected()
    {
        using var zip = new ZipFixture().Add("../escape.txt", "outside");
        using var temp = new TempDirectory();

        var extract = async () =>
            await new SafeExtractor(NullLogger<SafeExtractor>.Instance).ExtractAsync(
                zip.Path,
                temp.Combine("staging"),
                CancellationToken.None
            );

        await extract.Should().ThrowAsync<FileLinkException>();
        File.Exists(temp.Combine("escape.txt")).Should().BeFalse();
    }

    private static string Fixture(string name)
    {
        return Path.Join(AppContext.BaseDirectory, "Mods", "Fixtures", name);
    }

    private static ModArchiveFormat Detect(TempDirectory temp, string name, byte[] head)
    {
        var path = temp.Combine(name);
        File.WriteAllBytes(path, [.. head, 0x00, 0x00, 0x00, 0x00]);
        return ModArchiveContent.Detect(path);
    }
}
