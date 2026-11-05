using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Mods;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class ArchiveInspectorTests
{
    private readonly ArchiveInspector _inspector = new(NullLogger<ArchiveInspector>.Instance);

    [Fact]
    public void Inspect_ArchiveWrappedInOneFolder_StillFindsThePlugin()
    {
        // Most mods ship inside a wrapper folder; classification has to happen after it comes off, or the plugin is
        // seen as content and lands nowhere.
        using var archive = new ZipFixture()
            .AddAssembly("ModName/BepInEx/plugins/ModName.dll", "ClientMod.dll")
            .Add("ModName/readme.md", "hi");

        var inspection = _inspector.Inspect(archive.Path);

        inspection.RootPrefix.Should().Be("ModName");
        inspection.Kind.Should().Be(ModKind.Client);
        inspection.CandidateAssemblies.Should().Contain("BepInEx/plugins/ModName.dll");
        inspection.Entries.Should().Contain(entry => entry.NeedsRelocation);
    }

    [Fact]
    public void Inspect_ClientMod_ClassifiesThePluginAndItsConfig()
    {
        using var archive = new ZipFixture()
            .AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll")
            .Add("BepInEx/config/com.fixture.clientmod.cfg", "[General]");

        var inspection = _inspector.Inspect(archive.Path);

        inspection.Kind.Should().Be(ModKind.Client);
        inspection.CandidateAssemblies.Should().Contain("BepInEx/plugins/fixture/FixturePlugin.dll");
        inspection.Entries.Should().Contain(entry => entry.Kind == ArchiveEntryKind.Config);
        inspection.IsUnsafe.Should().BeFalse();
        inspection.RootPrefix.Should().BeNull();
    }

    [Fact]
    public void Inspect_ServerMod_ClassifiesTheServerAssembly()
    {
        using var archive = new ZipFixture()
            .AddAssembly("SPT_Runtime/user/mods/ServerFixture/FixtureServer.dll", "ServerMod.dll")
            .Add("SPT_Runtime/user/mods/ServerFixture/Presets/default.json", "{}");

        var inspection = _inspector.Inspect(archive.Path);

        inspection.Kind.Should().Be(ModKind.Server);
        inspection.CandidateAssemblies.Should().Contain("SPT_Runtime/user/mods/ServerFixture/FixtureServer.dll");
    }

    [Fact]
    public void Inspect_BothHalves_ReportsBoth()
    {
        using var archive = new ZipFixture()
            .AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll")
            .AddAssembly("SPT_Runtime/user/mods/fixture/FixtureServer.dll", "ServerMod.dll");

        _inspector.Inspect(archive.Path).Kind.Should().Be(ModKind.Both);
    }

    [Fact]
    public void Inspect_WrappedArchive_DetectsAndStripsTheRootPrefix()
    {
        using var archive = new ZipFixture().AddAssembly("DynamicMaps-1.2.1/BepInEx/plugins/mpstark/FixturePlugin.dll", "ClientMod.dll");

        var inspection = _inspector.Inspect(archive.Path);

        inspection.RootPrefix.Should().Be("DynamicMaps-1.2.1");
        inspection.Entries.Should().ContainSingle().Which.RelativePath.Should().Be("BepInEx/plugins/mpstark/FixturePlugin.dll");
        inspection.Entries[0].NeedsRelocation.Should().BeTrue();
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/absolute/escape.dll")]
    [InlineData("BepInEx/../../escape.dll")]
    public void Inspect_PathTraversal_IsMarkedUnsafe(string entryPath)
    {
        using var archive = new ZipFixture().Add(entryPath, "evil");

        var inspection = _inspector.Inspect(archive.Path);

        inspection.IsUnsafe.Should().BeTrue();
        inspection.UnsafeEntries.Should().NotBeEmpty();
        inspection.Entries.Should().Contain(entry => entry.Kind == ArchiveEntryKind.Unsafe);
    }

    [Fact]
    public void Inspect_APatchedGameBinary_IsMarkedUnsafe()
    {
        using var archive = new ZipFixture().Add("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", "evil");

        _inspector.Inspect(archive.Path).IsUnsafe.Should().BeTrue();
    }

    [Fact]
    public void Inspect_ReportsWhetherTheArchiveShipsBinaries()
    {
        using var withBinary = new ZipFixture().AddAssembly("BepInEx/plugins/x/FixturePlugin.dll", "ClientMod.dll");
        using var withoutBinary = new ZipFixture().Add("BepInEx/config/x.cfg", "[General]");

        _inspector.Inspect(withBinary.Path).ShipsBinaries.Should().BeTrue();
        _inspector.Inspect(withoutBinary.Path).ShipsBinaries.Should().BeFalse();
    }
}
