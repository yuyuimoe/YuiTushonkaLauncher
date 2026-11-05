using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Mods;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class ModIdentityReaderTests
{
    private readonly ModIdentityReader _reader = new(NullLogger<ModIdentityReader>.Instance);

    private static string FixtureAssembly(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, name);
    }

    [Fact]
    public void Read_ClientFixture_ReadsTheBepInPluginAttribute()
    {
        var identity = _reader.Read(FixtureAssembly("ClientMod.dll"), ModKind.Client);

        identity.Guid.Should().Be("com.fixture.clientmod");
        identity.Name.Should().Be("Fixture Client Mod");
        identity.Version.Should().Be(SemanticVersion.Parse("1.4.2"));
        identity.Kind.Should().Be(ModKind.Client);
        identity.Source.Should().Be(ModIdentitySource.BepInPluginAttribute);
        identity.HasGuid.Should().BeTrue();
    }

    [Fact]
    public void Read_ServerFixture_ReadsTheMetadataLiterals()
    {
        var identity = _reader.Read(FixtureAssembly("ServerMod.dll"), ModKind.Server);

        identity.Guid.Should().Be("com.fixture.servermod");
        identity.Version.Should().Be(SemanticVersion.Parse("2.0.1"));
        identity.Kind.Should().Be(ModKind.Server);
        identity.Source.Should().Be(ModIdentitySource.ServerModMetadata);
    }

    [Fact]
    public void Read_AnAssemblyWithoutModMetadata_ReportsNone()
    {
        var identity = _reader.Read(FixtureAssembly("YuiTushonkaLauncher.Core.dll"), ModKind.Client);

        identity.HasGuid.Should().BeFalse();
        identity.Source.Should().Be(ModIdentitySource.None);
        identity.Reason.Should().NotBeNull();
    }

    [Fact]
    public void Read_AMissingFile_DoesNotThrow()
    {
        var identity = _reader.Read(FixtureAssembly("does-not-exist.dll"), ModKind.Client);

        identity.HasGuid.Should().BeFalse();
        identity.Source.Should().Be(ModIdentitySource.None);
    }

    [Fact]
    public void Read_ANonAssemblyFile_DoesNotThrow()
    {
        using var temp = new Infrastructure.TempDirectory();
        var path = temp.Combine("fake.dll");
        File.WriteAllText(path, "not a PE file");

        var identity = _reader.Read(path, ModKind.Client);

        identity.Source.Should().Be(ModIdentitySource.None);
    }
}
