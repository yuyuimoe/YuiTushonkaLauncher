using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Runtime;

namespace YuiTushonkaLauncher.Tests.Runtime;

public sealed class PrefixInspectorTests
{
    [Fact]
    public void Inspect_ANewOrEmptyFolderIsMissing()
    {
        using var world = new RuntimeWorld();
        var empty = world.Combine("empty");
        Directory.CreateDirectory(empty);

        PrefixInspector.Inspect(world.Combine("nothing")).State.Should().Be(PrefixState.Missing);
        PrefixInspector.Inspect(empty).State.Should().Be(PrefixState.Missing);
    }

    [Fact]
    public void Inspect_RecognisesAWinePrefixAtTheRootAndInsideProtonsPfxFolder()
    {
        using var world = new RuntimeWorld();

        PrefixInspector.Inspect(world.CreatePrefix("umu-style")).State.Should().Be(PrefixState.Initialized);
        PrefixInspector.Inspect(world.CreatePrefix("proton-style", inPfx: true)).State.Should().Be(PrefixState.Initialized);
    }

    [Fact]
    public void Inspect_AFileWhereAFolderIsExpectedIsBroken()
    {
        using var world = new RuntimeWorld();
        var file = world.Combine("a-file");
        File.WriteAllText(file, "x");

        var inspection = PrefixInspector.Inspect(file);

        inspection.State.Should().Be(PrefixState.Broken);
        inspection.Reason.Should().Contain("is a file");
        inspection.LooksLikePrefix.Should().BeFalse();
    }

    [Fact]
    public void Inspect_APartialPrefixIsBrokenButRepairable()
    {
        using var world = new RuntimeWorld();
        var prefix = world.CreatePrefix("partial");
        File.Delete(Path.Combine(prefix, "user.reg"));

        var inspection = PrefixInspector.Inspect(prefix);

        inspection.State.Should().Be(PrefixState.Broken);
        inspection.LooksLikePrefix.Should().BeTrue();
        inspection.Reason.Should().Contain("user.reg").And.Contain("incomplete");
    }

    [Fact]
    public void Inspect_AnEmptyRegistryFileCountsAsMissing()
    {
        using var world = new RuntimeWorld();
        var prefix = world.CreatePrefix("truncated");
        File.WriteAllText(Path.Combine(prefix, "system.reg"), "");

        var inspection = PrefixInspector.Inspect(prefix);

        inspection.State.Should().Be(PrefixState.Broken);
        inspection.LooksLikePrefix.Should().BeTrue();
        inspection.Reason.Should().Contain("system.reg");
    }

    [Fact]
    public void Inspect_AFolderWithOtherFilesIsNotAPrefixAndIsNotRepairable()
    {
        using var world = new RuntimeWorld();
        var foreign = world.Combine("foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "thesis.docx"), "x");

        var inspection = PrefixInspector.Inspect(foreign);

        inspection.State.Should().Be(PrefixState.Broken);
        inspection.LooksLikePrefix.Should().BeFalse();
        inspection.Reason.Should().Contain("does not look like a Wine prefix");
    }

    [Fact]
    public void FindUnwritableFolder_IsNullWhereWritingWorksAndLeavesNothingBehind()
    {
        using var world = new RuntimeWorld();
        var target = world.Combine("a", "b", "prefix");

        PrefixInspector.FindUnwritableFolder(target).Should().BeNull();
        Directory.Exists(world.Combine("a")).Should().BeFalse("probing must not create the missing folders");
        Directory
            .EnumerateFileSystemEntries(world.Combine(""))
            .Should()
            .NotContain(entry => entry.Contains(".ytt-write-test", StringComparison.Ordinal));
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void FindUnwritableFolder_NamesTheNearestExistingFolderThatRefusesWrites()
    {
        if (Environment.UserName == "root")
        {
            return;
        }

        using var world = new RuntimeWorld();
        var locked = world.Combine("locked");
        Directory.CreateDirectory(locked);
        RuntimeWorld.Lock(locked);

        try
        {
            PrefixInspector.FindUnwritableFolder(Path.Combine(locked, "a", "b")).Should().Be(locked);
            PrefixInspector.FindUnwritableFolder(locked).Should().Be(locked);
        }
        finally
        {
            RuntimeWorld.Unlock(locked);
        }
    }
}
