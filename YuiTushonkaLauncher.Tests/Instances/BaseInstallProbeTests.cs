using System.Text;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

public sealed class BaseInstallProbeTests
{
    [Fact]
    public void Inspect_ReadsThe41LayoutAndVersions()
    {
        using var install = FakeBaseInstall.CreateSpt41("4.1.6");
        var probe = FakeBaseInstall.CreateProbe();

        var info = probe.Inspect(install.Root);

        info.Layout.Should().Be(InstanceLayout.Spt41Plus);
        info.RuntimeDirectory.AsSlashes().Should().Be("spt/SPT_Runtime");
        info.SptVersion.Should().Be(SemanticVersion.Parse("4.1.6"));
    }

    [Fact]
    public void Inspect_ReadsThe40Layout()
    {
        using var install = FakeBaseInstall.CreateSpt40("4.0.13");
        var probe = FakeBaseInstall.CreateProbe();

        var info = probe.Inspect(install.Root);

        info.Layout.Should().Be(InstanceLayout.Spt40);
        info.RuntimeDirectory.AsSlashes().Should().Be("spt/SPT");
        info.SptVersion.Should().Be(SemanticVersion.Parse("4.0.13"));
    }

    [Fact]
    public void Inspect_RejectsSomethingThatIsNotAnSptInstall()
    {
        using var temp = new Infrastructure.TempDirectory();
        var probe = FakeBaseInstall.CreateProbe();

        var act = () => probe.Inspect(temp.Path);

        act.Should().Throw<InstanceValidationException>().Which.Issues.Should().NotBeEmpty();
    }

    [Fact]
    public void Inspect_ReportsEveryMissingPiece()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.DeleteFile("gamefiles/EscapeFromTarkov.exe");
        install.DeleteFile("spt/BepInEx/core/BepInEx.Preloader.dll");
        var probe = FakeBaseInstall.CreateProbe();

        var act = () => probe.Inspect(install.Root);

        var exception = act.Should().Throw<InstanceValidationException>().Which;
        exception.Issues.Should().Contain(issue => issue.Contains("EscapeFromTarkov.exe"));
        exception.Issues.Should().Contain(issue => issue.Contains("BepInEx/core"));
    }

    [Fact]
    public void Inspect_WarnsButProceedsWhenDoorstopFilesAreMissing()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.DeleteFile("spt/winhttp.dll");
        install.DeleteFile("spt/doorstop_config.ini");
        var probe = FakeBaseInstall.CreateProbe();

        var info = probe.Inspect(install.Root);

        info.Warnings.Should().HaveCount(2);
        info.Warnings.Should().Contain(warning => warning.Contains("winhttp.dll"));
    }

    [Fact]
    public void Inspect_TreatsAMissingDepsJsonAsUnknownVersion()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.DeleteFile("spt/SPT_Runtime/SPT.Server.deps.json");
        var probe = FakeBaseInstall.CreateProbe();

        var info = probe.Inspect(install.Root);

        info.SptVersion.Should().BeNull();
        info.Warnings.Should().Contain(warning => warning.Contains("SPT.Server.deps.json"));
    }

    [Fact]
    public void Inspect_WarnsWhenDepsJsonHasNoLibraries()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.WriteFile("spt/SPT_Runtime/SPT.Server.deps.json", "{ \"runtimeTarget\": { } }");
        var probe = FakeBaseInstall.CreateProbe();

        var info = probe.Inspect(install.Root);

        info.SptVersion.Should().BeNull();
        info.Warnings.Should().Contain(warning => warning.Contains("libraries"));
    }

    [Fact]
    public void Inspect_WarnsWhenDepsJsonIsMalformed()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.WriteFile("spt/SPT_Runtime/SPT.Server.deps.json", "{ not json");
        var probe = FakeBaseInstall.CreateProbe();

        var info = probe.Inspect(install.Root);

        info.SptVersion.Should().BeNull();
        info.Warnings.Should().Contain(warning => warning.Contains("could not be read"));
    }

    [Fact]
    public void DetectLayout_ReturnsNullForAnEmptyDirectory()
    {
        using var temp = new Infrastructure.TempDirectory();

        BaseInstallProbe.DetectLayout(temp.Path, InstallShape.Split).Should().BeNull();
    }

    [Fact]
    public void PeVersionReader_ReadsFileVersionFromAVersionResource()
    {
        // Minimal VS_VERSIONINFO-like layout: "FileVersion\0" key, 4-byte alignment, UTF-16 value.
        var builder = new List<byte>();
        builder.AddRange(Encoding.Unicode.GetBytes("VS_VERSION_INFO"));
        builder.AddRange([0, 0]);
        builder.AddRange(Encoding.Unicode.GetBytes("FileVersion\0"));
        while (builder.Count % 4 != 0)
        {
            builder.Add(0);
        }

        builder.AddRange(Encoding.Unicode.GetBytes("0.16.9.40743\0"));

        PeVersionReader.TryReadFileVersion(builder.ToArray()).Should().Be("0.16.9.40743");
    }

    [Fact]
    public void PeVersionReader_ReturnsNullWhenThereIsNoVersionResource()
    {
        PeVersionReader.TryReadFileVersion(Encoding.ASCII.GetBytes("not a PE file")).Should().BeNull();
    }
}
