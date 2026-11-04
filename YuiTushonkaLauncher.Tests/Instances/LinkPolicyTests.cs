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
    [InlineData("create-spt-instance.sh", false, LinkAction.Skip)]
    [InlineData("something-else/config.json", false, LinkAction.Copy)]
    [InlineData("something-else", true, LinkAction.CreateDirectory)]
    public void Decide_FollowsTheDocumentedPolicy_ForASplitBase(string relativePath, bool isDirectory, LinkAction expected)
    {
        LinkPolicy.Decide(relativePath, isDirectory, InstallShape.Split).Should().Be(expected);
    }

    [Theory]
    [InlineData("EscapeFromTarkov.exe", false, LinkAction.Link)]
    [InlineData("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", false, LinkAction.Copy)]
    [InlineData("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", false, LinkAction.Copy)]
    [InlineData("UnityPlayer.dll", false, LinkAction.Link)]
    [InlineData("winhttp.dll", false, LinkAction.Link)]
    [InlineData("doorstop_config.ini", false, LinkAction.Link)]
    [InlineData("BepInEx/plugins/spt/spt-core.dll", false, LinkAction.Link)]
    [InlineData("BepInEx/plugins/MyMod/MyMod.dll", false, LinkAction.Skip)]
    [InlineData("BepInEx/config/BepInEx.cfg", false, LinkAction.Copy)]
    [InlineData("SPT_Runtime/SPT.Server.dll", false, LinkAction.Link)]
    [InlineData("SPT_Runtime/SPT_Data/configs/http.json", false, LinkAction.Copy)]
    [InlineData("SPT_Runtime/user/profiles/abc.json", false, LinkAction.Skip)]
    [InlineData("SPT/user/mods/Foo/Foo.dll", false, LinkAction.Skip)]
    [InlineData("Logs/log_2026.11.03/errors.log", false, LinkAction.Link)]
    [InlineData("Greed.exe", false, LinkAction.Copy)]
    public void Decide_FollowsTheSamePolicy_ForAMergedBase_WithoutTheWrappers(string relativePath, bool isDirectory, LinkAction expected)
    {
        LinkPolicy.Decide(relativePath, isDirectory, InstallShape.Merged).Should().Be(expected);
    }

    [Fact]
    public void Decide_IsCaseInsensitiveAndAcceptsBackslashes()
    {
        LinkPolicy
            .Decide(@"GAMEFILES\EscapeFromTarkov_Data\Managed\assembly-csharp.DLL", false, InstallShape.Split)
            .Should()
            .Be(LinkAction.Copy);
        LinkPolicy.Decide("spt/BepInEx/Core/BepInEx.dll", false, InstallShape.Split).Should().Be(LinkAction.Link);
        LinkPolicy.Decide(@"bepinex\core\BepInEx.Preloader.dll", false, InstallShape.Merged).Should().Be(LinkAction.Link);
    }

    [Fact]
    public void Decide_TreatsAnEmptyPathAsSkip()
    {
        LinkPolicy.Decide(string.Empty, isDirectory: true, InstallShape.Split).Should().Be(LinkAction.Skip);
        LinkPolicy.Decide(string.Empty, isDirectory: true, InstallShape.Merged).Should().Be(LinkAction.Skip);
    }

    [Fact]
    public void RequiredDirectories_AreInstancePaths_AndDependOnTheLayout()
    {
        var runtime41 = InstancePaths.RequiredDirectories(InstanceLayout.Spt41Plus);
        var runtime40 = InstancePaths.RequiredDirectories(InstanceLayout.Spt40);

        runtime41.Should().Contain("SPT_Runtime/user/mods").And.Contain("BepInEx/plugins").And.Contain("Logs");
        runtime41.Should().NotContain(directory => directory.StartsWith("spt/", StringComparison.Ordinal));
        runtime40.Should().Contain("SPT/user/mods").And.NotContain(directory => directory.Contains("SPT_Runtime"));
    }

    [Fact]
    public void RuntimeDirectory_IsTheMergedOne()
    {
        InstancePaths.RuntimeDirectory(InstanceLayout.Spt40).Should().Be("SPT");
        InstancePaths.RuntimeDirectory(InstanceLayout.Spt41Plus).Should().Be("SPT_Runtime");
    }

    [Theory]
    [InlineData("gamefiles/EscapeFromTarkov.exe", "EscapeFromTarkov.exe")]
    [InlineData("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")]
    [InlineData("spt/BepInEx/plugins/spt/spt-core.dll", "BepInEx/plugins/spt/spt-core.dll")]
    [InlineData("spt/SPT_Runtime/SPT_Data/configs/http.json", "SPT_Runtime/SPT_Data/configs/http.json")]
    [InlineData("gamefiles", "")]
    [InlineData("spt", "")]
    [InlineData("create-spt-instance.sh", "create-spt-instance.sh")]
    public void FromBase_UnwrapsASplitBase(string baseRelative, string expected)
    {
        InstancePaths.FromBase(baseRelative, InstallShape.Split).Should().Be(expected);
    }

    [Theory]
    [InlineData("EscapeFromTarkov.exe", "EscapeFromTarkov.exe")]
    [InlineData("SPT_Runtime/user/mods/Foo/Foo.dll", "SPT_Runtime/user/mods/Foo/Foo.dll")]
    [InlineData("BepInEx/plugins/spt/spt-core.dll", "BepInEx/plugins/spt/spt-core.dll")]
    public void FromBase_LeavesAMergedBaseAlone(string baseRelative, string expected)
    {
        InstancePaths.FromBase(baseRelative, InstallShape.Merged).Should().Be(expected);
    }

    [Fact]
    public void Detect_ReadsTheShapeFromWhatIsOnDisk()
    {
        using var temp = new Infrastructure.TempDirectory();
        var split = temp.Combine("split");
        var merged = temp.Combine("merged");
        var empty = temp.Combine("empty");

        Directory.CreateDirectory(Path.Combine(split, "gamefiles"));
        File.WriteAllText(Path.Combine(split, "gamefiles", "EscapeFromTarkov.exe"), "MZ");
        Directory.CreateDirectory(merged);
        File.WriteAllText(Path.Combine(merged, "EscapeFromTarkov.exe"), "MZ");
        Directory.CreateDirectory(empty);

        InstancePaths.Detect(split).Should().Be(InstallShape.Split);
        InstancePaths.Detect(merged).Should().Be(InstallShape.Merged);
        InstancePaths.Detect(empty).Should().BeNull();
        InstancePaths.Detect(temp.Combine("missing")).Should().BeNull();
    }
}
