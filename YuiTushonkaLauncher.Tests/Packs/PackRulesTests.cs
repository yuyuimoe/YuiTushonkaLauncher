using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Packs;

namespace YuiTushonkaLauncher.Tests.Packs;

/// <summary>The pure rules behind every "may a package contain this?" decision.</summary>
public sealed class PackRulesTests
{
    [Theory]
    [InlineData("BepInEx/config/com.mod.cfg")]
    [InlineData("SPT_Runtime/user/mods/[SVM] Server Value Modifier/config.json")]
    [InlineData("a.json")]
    [InlineData("dir with spaces/file name.json")]
    [InlineData("naïve/ünïcode.json")]
    [InlineData("a/b/c/d.toml")]
    public void IsSafeRelativePath_AcceptsOrdinaryInstancePaths(string path)
    {
        PackRules.IsSafeRelativePath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("../evil.json")]
    [InlineData("a/../../evil.json")]
    [InlineData("a/./b.json")]
    [InlineData("a//b.json")]
    [InlineData("/etc/passwd")]
    [InlineData("a/b/")]
    [InlineData("C:/Windows/evil.json")]
    [InlineData("C:evil.json")]
    [InlineData("c:\\evil.json")]
    [InlineData("a\\b.json")]
    [InlineData("\\\\server\\share\\x.json")]
    [InlineData("evil.json.")]
    [InlineData("evil.json ")]
    [InlineData(" evil.json")]
    [InlineData("CON")]
    [InlineData("con.json")]
    [InlineData("a/NUL.txt")]
    [InlineData("COM1.cfg")]
    [InlineData("lpt9.json")]
    [InlineData("a/b:stream.json")]
    [InlineData("a/b?.json")]
    [InlineData("a/b*.json")]
    [InlineData("a/b|c.json")]
    public void IsSafeRelativePath_RejectsAnythingThatCouldEscapeOrBeReinterpreted(string? path)
    {
        PackRules.IsSafeRelativePath(path).Should().BeFalse();
    }

    [Fact]
    public void IsSafeRelativePath_RejectsControlCharactersAndNul()
    {
        PackRules.IsSafeRelativePath("a\0b.json").Should().BeFalse();
        PackRules.IsSafeRelativePath("a\nb.json").Should().BeFalse();
        PackRules.IsSafeRelativePath("a\tb.json").Should().BeFalse();
    }

    [Fact]
    public void IsSafeRelativePath_RejectsAVeryLongPath()
    {
        PackRules.IsSafeRelativePath(new string('a', PackRules.MaxPathLength + 1) + ".json").Should().BeFalse();
    }

    [Fact]
    public void IsSafeRelativePath_AllowsComLookalikesThatAreNotDeviceNames()
    {
        PackRules.IsSafeRelativePath("COMMON.json").Should().BeTrue();
        PackRules.IsSafeRelativePath("COM10.json").Should().BeTrue();
        PackRules.IsSafeRelativePath("console.json").Should().BeTrue();
    }

    [Theory]
    [InlineData("a.json")]
    [InlineData("a.INI")]
    [InlineData("a.toml")]
    [InlineData("a.yaml")]
    [InlineData("a.yml")]
    [InlineData("a.txt")]
    [InlineData("a.cfg")]
    public void IsAllowedConfigExtension_AcceptsTheShareableTypes(string path)
    {
        PackRules.IsAllowedConfigExtension(path).Should().BeTrue();
        PackRules.IsForbiddenExtension(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("a.dll")]
    [InlineData("a.EXE")]
    [InlineData("a.bundle")]
    [InlineData("a.pak")]
    [InlineData("a.dat")]
    [InlineData("a.so")]
    [InlineData("a.dylib")]
    [InlineData("a.bat")]
    [InlineData("a.cmd")]
    [InlineData("a.sh")]
    [InlineData("a.ps1")]
    [InlineData("a.msi")]
    [InlineData("a.jar")]
    [InlineData("a.lnk")]
    public void IsForbiddenExtension_RefusesBinariesAndScripts(string path)
    {
        PackRules.IsForbiddenExtension(path).Should().BeTrue();
        PackRules.IsAllowedConfigExtension(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("a.xml")]
    [InlineData("a")]
    [InlineData("a.json.exe")]
    [InlineData("a.png")]
    public void IsAllowedConfigExtension_RejectsEverythingElse(string path)
    {
        PackRules.IsAllowedConfigExtension(path).Should().BeFalse();
    }

    [Fact]
    public void ConfigEntryName_PutsTheConfigUnderTheConfigsDirectory()
    {
        PackRules.ConfigEntryName("BepInEx/config/x.cfg").Should().Be("configs/BepInEx/config/x.cfg");
    }

    [Fact]
    public void PackException_CarriesTheProblemKind()
    {
        var exception = new PackException(PackProblem.Tampered, "edited");

        exception.Problem.Should().Be(PackProblem.Tampered);
        exception.Message.Should().Be("edited");
    }
}
