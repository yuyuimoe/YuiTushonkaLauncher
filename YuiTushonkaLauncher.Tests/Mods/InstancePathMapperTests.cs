using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Infrastructure.Mods;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class InstancePathMapperTests
{
    [Theory]
    // Game half.
    [InlineData(
        "EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll",
        "gamefiles/EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll"
    )]
    [InlineData("EscapeFromTarkov.exe", "gamefiles/EscapeFromTarkov.exe")]
    [InlineData("UnityPlayer.dll", "gamefiles/UnityPlayer.dll")]
    [InlineData("MonoBleedingEdge/EmbedRuntime/mono.dll", "gamefiles/MonoBleedingEdge/EmbedRuntime/mono.dll")]
    // SPT half.
    [InlineData("BepInEx/plugins/mod/ext.dll", "spt/BepInEx/plugins/mod/ext.dll")]
    [InlineData("BepInEx/config/mod.cfg", "spt/BepInEx/config/mod.cfg")]
    [InlineData("winhttp.dll", "spt/winhttp.dll")]
    [InlineData("doorstop_config.ini", "spt/doorstop_config.ini")]
    [InlineData("SPT_Runtime/user/mods/mod/mod.dll", "spt/SPT_Runtime/user/mods/mod/mod.dll")]
    [InlineData("SPT_Runtime/SPT_Data/database/x.json", "spt/SPT_Runtime/SPT_Data/database/x.json")]
    [InlineData("user/mods/mod/mod.dll", "spt/SPT_Runtime/user/mods/mod/mod.dll")]
    // Already instance-relative.
    [InlineData("gamefiles/EscapeFromTarkov_Data/Managed/x.dll", "gamefiles/EscapeFromTarkov_Data/Managed/x.dll")]
    [InlineData("spt/BepInEx/plugins/mod/mod.dll", "spt/BepInEx/plugins/mod/mod.dll")]
    // Relocated by the caller.
    [InlineData("readme.txt", "readme.txt")]
    public void Map_MovesGameRootPathsIntoTheInstanceLayout(string input, string expected)
    {
        InstancePathMapper.Map(input, InstanceLayout.Spt41Plus).Should().Be(expected);
    }

    [Fact]
    public void Map_UsesTheRuntimeDirectoryOfTheLayout()
    {
        InstancePathMapper.Map("SPT_Runtime/user/mods/mod/mod.dll", InstanceLayout.Spt40).Should().Be("spt/SPT/user/mods/mod/mod.dll");
        InstancePathMapper.Map("SPT/user/mods/mod/mod.dll", InstanceLayout.Spt41Plus).Should().Be("spt/SPT/user/mods/mod/mod.dll");
        InstancePathMapper.Map("user/profiles/a.json", InstanceLayout.Spt40).Should().Be("spt/SPT/user/profiles/a.json");
    }

    [Fact]
    public void Map_IsCaseInsensitiveAndAcceptsBackslashes()
    {
        InstancePathMapper.Map(@"bepinex\PLUGINS\mod\Mod.dll", InstanceLayout.Spt41Plus).Should().Be("spt/bepinex/PLUGINS/mod/Mod.dll");
        InstancePathMapper
            .Map("escapefromtarkov_data/Managed/x.dll", InstanceLayout.Spt41Plus)
            .Should()
            .Be("gamefiles/escapefromtarkov_data/Managed/x.dll");
    }

    [Theory]
    [InlineData("BepInEx/plugins/mod/mod.dll", true)]
    [InlineData("EscapeFromTarkov_Data/Managed/x.dll", true)]
    [InlineData("readme.txt", false)]
    [InlineData("docs/manual.pdf", false)]
    public void IsLayoutPath_TellsRelocationFromPlacement(string input, bool expected)
    {
        InstancePathMapper.IsLayoutPath(input, InstanceLayout.Spt41Plus).Should().Be(expected);
    }

    [Fact]
    public void DefaultModFolder_SanitisesTheGuid()
    {
        InstancePathMapper.DefaultModFolder("com.mpstark.dynamicmaps").Should().Be("com.mpstark.dynamicmaps");
        InstancePathMapper.DefaultModFolder("Weird Guid!/With Spaces").Should().Be("weird-guid--with-spaces");
        InstancePathMapper.DefaultModFolder("...").Should().Be("mod");
    }

    [Fact]
    public void ModRoot_DependsOnKindAndLayout()
    {
        InstancePathMapper.ModRoot(ModKind.Server, InstanceLayout.Spt41Plus, "x").Should().Be("spt/SPT_Runtime/user/mods/x");
        InstancePathMapper.ModRoot(ModKind.Server, InstanceLayout.Spt40, "x").Should().Be("spt/SPT/user/mods/x");
        InstancePathMapper.ModRoot(ModKind.Client, InstanceLayout.Spt41Plus, "x").Should().Be("spt/BepInEx/plugins/x");
    }
}
