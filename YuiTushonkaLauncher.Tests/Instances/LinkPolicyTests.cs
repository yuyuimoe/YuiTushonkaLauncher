using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher.Tests.Instances;

public sealed class LinkPolicyTests
{
    [Theory]
    [InlineData("gamefiles/EscapeFromTarkov.exe", false, LinkAction.Link)]
    [InlineData("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", false, LinkAction.Copy)]
    [InlineData("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", false, LinkAction.Copy)]
    [InlineData("gamefiles/EscapeFromTarkov_Data/Resources/foo.bundle", false, LinkAction.Link)]
    [InlineData("gamefiles/EscapeFromTarkov_Data", true, LinkAction.CreateDirectory)]
    [InlineData("spt/winhttp.dll", false, LinkAction.Link)]
    [InlineData("spt/BepInEx/core/BepInEx.Preloader.dll", false, LinkAction.Link)]
    [InlineData("spt/BepInEx/plugins/spt/spt-core.dll", false, LinkAction.Link)]
    [InlineData("spt/BepInEx/plugins/SomeUserMod/SomeUserMod.dll", false, LinkAction.Skip)]
    [InlineData("spt/BepInEx/config/BepInEx.cfg", false, LinkAction.Copy)]
    [InlineData("spt/BepInEx/cache/chainloader.dat", false, LinkAction.Skip)]
    [InlineData("spt/SPT_Runtime/SPT.Server.dll", false, LinkAction.Link)]
    [InlineData("spt/SPT_Runtime/SPT_Data/database/templates/items.json", false, LinkAction.Link)]
    [InlineData("spt/SPT_Runtime/SPT_Data/configs/core.json", false, LinkAction.Copy)]
    [InlineData("spt/SPT_Runtime/user/profiles/abc.json", false, LinkAction.Skip)]
    [InlineData("spt/SPT/user/profiles/abc.json", false, LinkAction.Skip)]
    [InlineData("spt/SPT/SPT.Server.dll", false, LinkAction.Link)]
    [InlineData("something-else/config.json", false, LinkAction.Copy)]
    [InlineData("something-else", true, LinkAction.CreateDirectory)]
    public void Decide_FollowsTheDocumentedPolicy(string relativePath, bool isDirectory, LinkAction expected)
    {
        LinkPolicy.Decide(relativePath, isDirectory).Should().Be(expected);
    }

    [Fact]
    public void Decide_IsCaseInsensitiveAndAcceptsBackslashes()
    {
        LinkPolicy.Decide(@"GAMEFILES\EscapeFromTarkov_Data\Managed\assembly-csharp.DLL", false).Should().Be(LinkAction.Copy);
        LinkPolicy.Decide("SPT/BepInEx/Core/BepInEx.dll", false).Should().Be(LinkAction.Link);
    }

    [Fact]
    public void Decide_TreatsAnEmptyPathAsSkip()
    {
        LinkPolicy.Decide(string.Empty, isDirectory: true).Should().Be(LinkAction.Skip);
    }

    [Fact]
    public void RequiredDirectories_DependOnTheLayout()
    {
        var runtime41 = LinkPolicy.RequiredDirectories(InstanceLayout.Spt41Plus);
        var runtime40 = LinkPolicy.RequiredDirectories(InstanceLayout.Spt40);

        runtime41.Should().Contain("spt/SPT_Runtime/user/mods");
        runtime41.Should().Contain("spt/BepInEx/plugins");
        runtime40.Should().Contain("spt/SPT/user/mods");
        runtime40.Should().NotContain(directory => directory.Contains("SPT_Runtime"));
    }

    [Fact]
    public void RuntimeDirectory_MatchesTheLayout()
    {
        LinkPolicy.RuntimeDirectory(InstanceLayout.Spt40).Should().Be("spt/SPT");
        LinkPolicy.RuntimeDirectory(InstanceLayout.Spt41Plus).Should().Be("spt/SPT_Runtime");
    }
}
