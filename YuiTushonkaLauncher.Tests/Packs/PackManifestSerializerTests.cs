using System.Text;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Infrastructure.Packs;

namespace YuiTushonkaLauncher.Tests.Packs;

/// <summary>The canonical form is what makes the share code stable, so it is pinned down byte by byte.</summary>
public sealed class PackManifestSerializerTests
{
    [Fact]
    public void Serialize_WritesSortedKeysLfLineEndingsAndNoBom()
    {
        var bytes = PackManifestSerializer.Serialize(PackCrafter.Manifest());
        var text = Encoding.UTF8.GetString(bytes);

        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "no BOM");
        text.Should().NotContain("\r");
        text.Should().EndWith("}\n");

        var keys = System
            .Text.RegularExpressions.Regex.Matches(text, "^  \"(\\w+)\":", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToList();
        keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        keys.Should().Contain(["appVersion", "formatVersion", "layout", "mods", "shareCode", "sptVersion"]);
    }

    [Fact]
    public void Serialize_IsDeterministic()
    {
        var manifest = PackCrafter.Manifest();

        PackManifestSerializer.Serialize(manifest).Should().Equal(PackManifestSerializer.Serialize(manifest));
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var manifest = PackCrafter.Manifest(
            [PackCrafter.Mod(), PackCrafter.Mod("com.other", "2.0.0", null)],
            [PackCrafter.Config("com.fixture.clientmod", "BepInEx/config/x.cfg", "a=1")]
        );

        var parsed = PackManifestSerializer.Deserialize(PackManifestSerializer.Serialize(manifest));

        parsed.Should().BeEquivalentTo(manifest);
    }

    [Fact]
    public void ShareCode_IgnoresWhenWhoAndWhereItWasExported()
    {
        var manifest = PackCrafter.Manifest();
        var other = manifest with
        {
            CreatedAt = manifest.CreatedAt.AddYears(3),
            AppVersion = "9.9.9",
            InstanceName = "someone else's instance",
        };

        PackManifestSerializer.ComputeShareCode(other).Should().Be(manifest.ShareCode);
    }

    [Fact]
    public void ShareCode_IgnoresDisplayNames_WhichSayNothingAboutWhatIsInstalled()
    {
        var manifest = PackCrafter.Manifest();
        var renamed = manifest with { Mods = [.. manifest.Mods.Select(mod => mod with { Name = "A Completely Different Name" })] };

        PackManifestSerializer.ComputeShareCode(renamed).Should().Be(manifest.ShareCode);
    }

    [Fact]
    public void ShareCode_ChangesWithTheComposition()
    {
        var manifest = PackCrafter.Manifest();

        PackManifestSerializer.ComputeShareCode(manifest with { SptVersion = "4.0.13" }).Should().NotBe(manifest.ShareCode);
        PackManifestSerializer
            .ComputeShareCode(manifest with { Mods = [PackCrafter.Mod(version: "1.4.3")] })
            .Should()
            .NotBe(manifest.ShareCode);
    }

    [Fact]
    public void ShareCode_IsA64CharacterLowercaseHexDigest()
    {
        PackCrafter.Manifest().ShareCode.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData("{\"formatVersion\":1}", 1)]
    [InlineData("{\"formatVersion\":99,\"somethingNew\":[1,2]}", 99)]
    [InlineData("{\"formatVersion\":\"1\"}", null)]
    [InlineData("{\"formatVersion\":1.5}", null)]
    [InlineData("{}", null)]
    [InlineData("[]", null)]
    [InlineData("not json", null)]
    public void ReadFormatVersion_ReadsOnlyThatField(string json, int? expected)
    {
        PackManifestSerializer.ReadFormatVersion(Encoding.UTF8.GetBytes(json)).Should().Be(expected);
    }

    [Fact]
    public void Deserialize_RejectsAnUnknownMemberAtFormatOne()
    {
        var json = Encoding
            .UTF8.GetString(PackManifestSerializer.Serialize(PackCrafter.Manifest()))
            .Replace("\"mods\"", "\"surprise\": 1,\n  \"mods\"");

        var act = () => PackManifestSerializer.Deserialize(Encoding.UTF8.GetBytes(json));

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void Deserialize_RejectsAMissingRequiredMember()
    {
        var act = () => PackManifestSerializer.Deserialize(Encoding.UTF8.GetBytes("{\"formatVersion\":1}"));

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void Deserialize_RejectsEmptyJsonNull()
    {
        var act = () => PackManifestSerializer.Deserialize(Encoding.UTF8.GetBytes("null"));

        act.Should().Throw<System.Text.Json.JsonException>();
    }
}
