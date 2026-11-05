using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Infrastructure.Mods;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class InstancePathMapperTests
{
    [Theory]
    // An archive is rooted at the game root, which is the instance root: nothing moves.
    [InlineData("EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll", "EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll")]
    [InlineData("EscapeFromTarkov.exe", "EscapeFromTarkov.exe")]
    [InlineData("UnityPlayer.dll", "UnityPlayer.dll")]
    [InlineData("MonoBleedingEdge/EmbedRuntime/mono.dll", "MonoBleedingEdge/EmbedRuntime/mono.dll")]
    [InlineData("BepInEx/plugins/mod/ext.dll", "BepInEx/plugins/mod/ext.dll")]
    [InlineData("BepInEx/config/mod.cfg", "BepInEx/config/mod.cfg")]
    [InlineData("winhttp.dll", "winhttp.dll")]
    [InlineData("doorstop_config.ini", "doorstop_config.ini")]
    [InlineData("SPT_Runtime/user/mods/mod/mod.dll", "SPT_Runtime/user/mods/mod/mod.dll")]
    [InlineData("SPT_Runtime/SPT_Data/database/x.json", "SPT_Runtime/SPT_Data/database/x.json")]
    // A bare "user/..." means the runtime's user folder, and a loose archive may still say "spt/".
    [InlineData("user/mods/mod/mod.dll", "SPT_Runtime/user/mods/mod/mod.dll")]
    [InlineData("spt/BepInEx/plugins/mod/mod.dll", "BepInEx/plugins/mod/mod.dll")]
    [InlineData("gamefiles/EscapeFromTarkov_Data/Managed/x.dll", "EscapeFromTarkov_Data/Managed/x.dll")]
    // Relocated by the caller.
    [InlineData("readme.txt", "readme.txt")]
    public void Map_KeepsTheLayoutPathsOfAMergedInstance(string input, string expected)
    {
        InstancePathMapper.Map(input, InstanceLayout.Spt41Plus).Should().Be(expected);
    }

    [Fact]
    public void Map_RenamesTheRuntimeDirectoryToTheInstancesOne()
    {
        InstancePathMapper.Map("SPT_Runtime/user/mods/mod/mod.dll", InstanceLayout.Spt40).Should().Be("SPT/user/mods/mod/mod.dll");
        InstancePathMapper.Map("SPT/user/mods/mod/mod.dll", InstanceLayout.Spt41Plus).Should().Be("SPT_Runtime/user/mods/mod/mod.dll");
        InstancePathMapper.Map("user/profiles/a.json", InstanceLayout.Spt40).Should().Be("SPT/user/profiles/a.json");
    }

    [Fact]
    public void Map_NormalisesCaseAndBackslashesWithoutChangingTheRoot()
    {
        InstancePathMapper.Map(@"bepinex\PLUGINS\mod\Mod.dll", InstanceLayout.Spt41Plus).Should().Be("bepinex/PLUGINS/mod/Mod.dll");
        InstancePathMapper
            .Map("escapefromtarkov_data/Managed/x.dll", InstanceLayout.Spt41Plus)
            .Should()
            .Be("escapefromtarkov_data/Managed/x.dll");
    }

    [Theory]
    [InlineData("BepInEx/plugins/mod/mod.dll", true)]
    [InlineData("EscapeFromTarkov_Data/Managed/x.dll", true)]
    [InlineData("SPT_Runtime/user/mods/x/x.dll", true)]
    [InlineData("Logs/log_2026/errors.log", true)]
    [InlineData("winhttp.dll", true)]
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
    public void ModRoot_IsAnInstancePath()
    {
        InstancePathMapper.ModRoot(ModKind.Server, InstanceLayout.Spt41Plus, "x").Should().Be("SPT_Runtime/user/mods/x");
        InstancePathMapper.ModRoot(ModKind.Server, InstanceLayout.Spt40, "x").Should().Be("SPT/user/mods/x");
        InstancePathMapper.ModRoot(ModKind.Client, InstanceLayout.Spt41Plus, "x").Should().Be("BepInEx/plugins/x");
    }
}
