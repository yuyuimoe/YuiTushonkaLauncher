using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Infrastructure.Packs;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Packs;

/// <summary>
/// A package is untrusted input. Each attack here is a hand-made zip that the reader must refuse with a
/// specific problem, before anything reaches the instance.
/// </summary>
public sealed class PackReaderTests : IDisposable
{
    private const string Owner = "com.fixture.clientmod";
    private const string ConfigPath = "BepInEx/config/com.fixture.clientmod.cfg";

    private readonly TempDirectory _temp = new();
    private readonly PackReader _reader = new(NullLogger<PackReader>.Instance);

    private string NewPath()
    {
        return _temp.Combine(Guid.NewGuid().ToString("n") + ".sptpack");
    }

    private PackException Refused(string package)
    {
        var act = () => _reader.Read(package);

        return act.Should().Throw<PackException>().Which;
    }

    [Fact]
    public void Read_AValidPackage_ReturnsTheManifestAndVerifiedConfigs()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(Owner, ConfigPath, "a=1")]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest), PackCrafter.ConfigEntry(ConfigPath, "a=1"));

        var contents = _reader.Read(path);

        contents.Manifest.Should().BeEquivalentTo(manifest);
        Encoding.UTF8.GetString(contents.Configs[ConfigPath]).Should().Be("a=1");
    }

    [Fact]
    public void Read_APackageWrittenByPackWriter_IsAccepted()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(Owner, ConfigPath, "a=1")]);
        var path = NewPath();

        PackWriter.Write(path, manifest, new Dictionary<string, byte[]> { [ConfigPath] = Encoding.UTF8.GetBytes("a=1") });

        _reader.Read(path).Manifest.ShareCode.Should().Be(manifest.ShareCode);
    }

    [Fact]
    public void Read_AMissingFile_IsMalformed()
    {
        Refused(_temp.Combine("nope.sptpack")).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_SomethingThatIsNotAZip_IsMalformed()
    {
        var path = NewPath();
        File.WriteAllText(path, "definitely not a zip");

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AZipWithoutAManifest_IsMalformed()
    {
        var path = PackCrafter.Zip(NewPath(), new PackCrafter.Entry("README.txt", [1]));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Theory]
    [InlineData("configs/../../evil.json")]
    [InlineData("configs/a/../../../evil.json")]
    [InlineData("configs/spt/../../x.json")]
    [InlineData("configs//absolute.json")]
    [InlineData("configs/C:/Windows/evil.json")]
    [InlineData("configs/CON.json")]
    [InlineData("configs/evil.json.")]
    public void Read_AConfigPathThatEscapes_IsUnsafe(string entryName)
    {
        var manifest = PackCrafter.Manifest();
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest), new PackCrafter.Entry(entryName, [1, 2, 3]));

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Theory]
    [InlineData("/evil.json")]
    [InlineData("..\\evil.json")]
    [InlineData("configs\\..\\evil.json")]
    [InlineData("../evil.json")]
    public void Read_AnEntryNameThatIsAbsoluteOrUsesBackslashes_IsUnsafe(string entryName)
    {
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(PackCrafter.Manifest()), new PackCrafter.Entry(entryName, [1]));

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Theory]
    [InlineData("configs/evil.dll")]
    [InlineData("evil.exe")]
    [InlineData("configs/sub/native.so")]
    [InlineData("BepInEx/plugins/mod.dll")]
    [InlineData("configs/run.bat")]
    [InlineData("configs/x.bundle")]
    public void Read_ABinaryAnywhere_IsRefusedAsForbiddenNotJustUnexpected(string entryName)
    {
        var path = PackCrafter.Zip(
            NewPath(),
            PackCrafter.ManifestEntry(PackCrafter.Manifest()),
            new PackCrafter.Entry(entryName, [0x4D, 0x5A])
        );

        var problem = Refused(path);

        problem.Problem.Should().Be(PackProblem.ForbiddenFile);
        problem.Message.Should().Contain("binary");
    }

    [Fact]
    public void Read_ASymbolicLinkEntry_IsUnsafe()
    {
        const int symlinkMode = 0xA000 | 0x1FF;
        var path = PackCrafter.Zip(
            NewPath(),
            PackCrafter.ManifestEntry(PackCrafter.Manifest()),
            new PackCrafter.Entry("configs/link.json", Encoding.UTF8.GetBytes("/etc/passwd"), symlinkMode)
        );

        var problem = Refused(path);

        problem.Problem.Should().Be(PackProblem.UnsafeEntry);
        problem.Message.Should().Contain("symbolic link");
    }

    [Fact]
    public void Read_AnEntryOutsideTheAllowedLayout_IsUnsafe()
    {
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(PackCrafter.Manifest()), new PackCrafter.Entry("extra.txt", [1]));

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Fact]
    public void Read_AConfigOfAnUnshareableType_IsUnsafe()
    {
        var path = PackCrafter.Zip(
            NewPath(),
            PackCrafter.ManifestEntry(PackCrafter.Manifest()),
            new PackCrafter.Entry("configs/spt/x.xml", [1])
        );

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Fact]
    public void Read_ADirectoryEntryOutsideConfigs_IsUnsafe()
    {
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(PackCrafter.Manifest()), new PackCrafter.Entry("../outside/", []));

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Fact]
    public void Read_ConfigsDirectoryEntries_AreIgnored()
    {
        var path = PackCrafter.Zip(
            NewPath(),
            PackCrafter.ManifestEntry(PackCrafter.Manifest()),
            new PackCrafter.Entry("configs/", []),
            new PackCrafter.Entry("configs/spt/", [])
        );

        _reader.Read(path).Configs.Should().BeEmpty();
    }

    [Fact]
    public void Read_TwoEntriesThatDifferOnlyInCase_AreUnsafe()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(Owner, ConfigPath, "a")]);
        var path = PackCrafter.Zip(
            NewPath(),
            PackCrafter.ManifestEntry(manifest),
            PackCrafter.ConfigEntry(ConfigPath, "a"),
            PackCrafter.ConfigEntry(ConfigPath.ToUpperInvariant(), "b")
        );

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Fact]
    public void Read_ANewerFormat_FailsWithAnUpgradeHintInsteadOfASchemaError()
    {
        var manifest = Encoding.UTF8.GetBytes("{\"formatVersion\":99,\"aFieldFromTheFuture\":{\"nested\":true}}");
        var path = PackCrafter.Zip(NewPath(), new PackCrafter.Entry("manifest.json", manifest));

        var problem = Refused(path);

        problem.Problem.Should().Be(PackProblem.FormatTooNew);
        problem.Message.Should().Contain("99").And.Contain("Update the launcher");
    }

    [Theory]
    [InlineData("{\"mods\":[]}")]
    [InlineData("{\"formatVersion\":0}")]
    [InlineData("{\"formatVersion\":-3}")]
    [InlineData("{\"formatVersion\":\"one\"}")]
    [InlineData("not json at all")]
    public void Read_AManifestWithoutAUsableFormatVersion_IsMalformed(string json)
    {
        var path = PackCrafter.Zip(NewPath(), new PackCrafter.Entry("manifest.json", Encoding.UTF8.GetBytes(json)));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_FormatOneWithAnUnknownField_IsMalformed()
    {
        var json = Encoding
            .UTF8.GetString(PackManifestSerializer.Serialize(PackCrafter.Manifest()))
            .Replace("\"mods\"", "\"hidden\": true,\n  \"mods\"");
        var path = PackCrafter.Zip(NewPath(), new PackCrafter.Entry("manifest.json", Encoding.UTF8.GetBytes(json)));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AnEditedManifest_FailsTheShareCode()
    {
        var manifest = PackCrafter.Manifest();
        var edited = manifest with { Mods = [PackCrafter.Mod(version: "9.9.9")] };
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(edited with { ShareCode = manifest.ShareCode }));

        Refused(path).Problem.Should().Be(PackProblem.Tampered);
    }

    [Fact]
    public void Read_AConfigWhoseContentWasChanged_FailsItsHash()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(Owner, ConfigPath, "a=1")]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest), PackCrafter.ConfigEntry(ConfigPath, "a=EVIL"));

        Refused(path).Problem.Should().Be(PackProblem.Tampered);
    }

    [Fact]
    public void Read_AConfigTheManifestDoesNotDeclare_IsUnsafe()
    {
        var path = PackCrafter.Zip(
            NewPath(),
            PackCrafter.ManifestEntry(PackCrafter.Manifest()),
            PackCrafter.ConfigEntry(ConfigPath, "smuggled")
        );

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Fact]
    public void Read_AConfigTheManifestDeclaresButTheZipLacks_IsMalformed()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(Owner, ConfigPath, "a=1")]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AConfigOwnedBySomethingThePackageDoesNotList_IsMalformed()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config("com.stranger", ConfigPath, "a=1")]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest), PackCrafter.ConfigEntry(ConfigPath, "a=1"));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AManifestDeclaringAnEscapingConfigPath_IsUnsafeEvenWithoutAnEntry()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(Owner, "../../evil.json", "x")]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest));

        Refused(path).Problem.Should().Be(PackProblem.UnsafeEntry);
    }

    [Fact]
    public void Read_AManifestDeclaringABinaryConfig_IsForbidden()
    {
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(Owner, "BepInEx/plugins/x/Mod.dll", "x")]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest));

        Refused(path).Problem.Should().Be(PackProblem.ForbiddenFile);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    public void Read_AModWithAnUnreadableVersion_IsMalformed(string version)
    {
        var manifest = PackCrafter.Manifest([PackCrafter.Mod(version: version)]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AModWithoutAGuid_IsMalformed()
    {
        var manifest = PackCrafter.Manifest([PackCrafter.Mod(guid: " ")]);
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(manifest));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AnAddonWhoseParentIsNotListed_IsMalformed()
    {
        var addon = new PackAddon
        {
            ParentGuid = "com.nobody",
            Guid = "addon.x",
            Name = "x",
            Version = "1.0.0",
        };
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(PackCrafter.Manifest(addons: [addon])));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AnInvalidContentDigest_IsMalformed()
    {
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(PackCrafter.Manifest([PackCrafter.Mod(digest: "not-a-hash")])));

        Refused(path).Problem.Should().Be(PackProblem.Malformed);
    }

    [Fact]
    public void Read_AnOversizedManifest_IsTooLarge_EvenWhenItCompressesToAFewBytes()
    {
        var padding = new byte[PackRules.MaxManifestBytes + 1024];
        Array.Fill(padding, (byte)' ');
        var path = PackCrafter.Zip(NewPath(), new PackCrafter.Entry("manifest.json", padding));

        new FileInfo(path).Length.Should().BeLessThan(10 * 1024, "this is a zip bomb: tiny on disk, huge expanded");
        Refused(path).Problem.Should().Be(PackProblem.TooLarge);
    }

    [Fact]
    public void Read_AnOversizedConfig_IsTooLarge()
    {
        var manifest = PackCrafter.Manifest();
        var path = PackCrafter.Zip(
            NewPath(),
            PackCrafter.ManifestEntry(manifest),
            new PackCrafter.Entry("configs/big.json", new byte[PackRules.MaxConfigBytes + 1])
        );

        Refused(path).Problem.Should().Be(PackProblem.TooLarge);
    }

    [Fact]
    public void Read_ManyLargeConfigs_AreTooLargeInTotal()
    {
        var entries = new List<PackCrafter.Entry> { PackCrafter.ManifestEntry(PackCrafter.Manifest()) };
        for (var index = 0; index < 14; index++)
        {
            entries.Add(new PackCrafter.Entry($"configs/part{index}.json", new byte[PackRules.MaxConfigBytes]));
        }

        var path = PackCrafter.Zip(NewPath(), [.. entries]);

        Refused(path).Problem.Should().Be(PackProblem.TooLarge);
    }

    [Fact]
    public void Read_TooManyEntries_IsTooLarge()
    {
        var entries = new List<PackCrafter.Entry> { PackCrafter.ManifestEntry(PackCrafter.Manifest()) };
        for (var index = 0; index <= PackRules.MaxEntries; index++)
        {
            entries.Add(new PackCrafter.Entry($"configs/{index}.json", []));
        }

        var path = PackCrafter.Zip(NewPath(), [.. entries]);

        Refused(path).Problem.Should().Be(PackProblem.TooLarge);
    }

    [Fact]
    public void Read_MoreModsThanAllowed_IsTooLarge()
    {
        var mods = Enumerable.Range(0, PackRules.MaxMods + 1).Select(index => PackCrafter.Mod($"com.m{index}", "1.0.0", index)).ToList();
        var path = PackCrafter.Zip(NewPath(), PackCrafter.ManifestEntry(PackCrafter.Manifest(mods)));

        Refused(path).Problem.Should().Be(PackProblem.TooLarge);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
