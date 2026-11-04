using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

/// <summary>
/// The detector is what makes Create and Adopt version-aware: it reads the SPT release, the game build that release
/// requires and the build that is actually present, all from the install itself.
/// </summary>
public sealed class InstallDetectorTests
{
    private static InstallDetector Detector()
    {
        return new InstallDetector(new PatchStateDetector(NullLogger<PatchStateDetector>.Instance), NullLogger<InstallDetector>.Instance);
    }

    [Fact]
    public void Detect_ReadsSptRequiredAndActualTarkovFromASplitInstall()
    {
        using var install = FakeBaseInstall.CreateSpt41();

        var fingerprint = Detector().Detect(install.Root);

        fingerprint.IsInstall.Should().BeTrue();
        fingerprint.Shape.Should().Be(InstallShape.Split);
        fingerprint.Layout.Should().Be(Core.Domain.InstanceLayout.Spt41Plus);
        fingerprint.RuntimeDirectory.Should().Be(Path.Combine("spt", "SPT_Runtime"));
        fingerprint.SptVersion!.ToString().Should().Be("4.1.6");
        fingerprint.RequiredTarkovVersion.Should().Be("0.16.9.40743");
        fingerprint.ActualTarkovVersion.Should().Be("0.16.9.40743");
        fingerprint.TarkovMatches.Should().BeTrue();
        fingerprint.IsRunnable.Should().BeFalse("a split install is the installer's output, not something SPT runs");
    }

    [Fact]
    public void Detect_ReportsTheMismatchWhenTheGameIsAnotherBuild()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.WriteFile("spt/SPT_Runtime/SPT_Data/configs/core.json", """{ "compatibleTarkovVersion": "0.16.9.40087" }""");

        var fingerprint = Detector().Detect(install.Root);

        fingerprint.TarkovMatches.Should().BeFalse();
        fingerprint.RequiredTarkovVersion.Should().Be("0.16.9.40087");
        fingerprint.ActualTarkovVersion.Should().Be("0.16.9.40743");
        fingerprint.Describe().Should().Contain("0.16.9.40087").And.Contain("0.16.9.40743");
    }

    [Fact]
    public void Detect_UnderstandsAMergedRunnableInstall()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("merged");
        WriteMergedInstall(temp, root);

        var fingerprint = Detector().Detect(root);

        fingerprint.IsInstall.Should().BeTrue();
        fingerprint.Shape.Should().Be(InstallShape.Merged);
        fingerprint.IsRunnable.Should().BeTrue();
        fingerprint.ActualTarkovVersion.Should().Be("0.16.9.40743");
    }

    [Fact]
    public void Detect_DoesNotThrowForAFolderThatIsNotAnInstall()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "hello");

        var fingerprint = Detector().Detect(temp.Path);

        fingerprint.IsInstall.Should().BeFalse();
        fingerprint.SptVersion.Should().BeNull();
        fingerprint.Warnings.Should().ContainSingle().Which.Should().Contain("not an SPT install");
    }

    [Fact]
    public void Detect_DoesNotThrowForAMissingFolderOrAnEmptyPath()
    {
        Detector().Detect("/nonexistent/ytt-install").IsInstall.Should().BeFalse();
        Detector().Detect(string.Empty).Warnings.Should().ContainSingle().Which.Should().Contain("No folder");
    }

    [Fact]
    public void Detect_WarnsWhenTheRequirementAndTheVersionManifestAreMissing()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.DeleteFile("spt/SPT_Runtime/SPT_Data/configs/core.json");
        install.WriteFile("spt/SPT_Runtime/SPT.Server.deps.json", "{ \"runtimeTarget\": { } }");

        var fingerprint = Detector().Detect(install.Root);

        fingerprint.RequiredTarkovVersion.Should().BeNull();
        fingerprint.SptVersion.Should().BeNull();
        fingerprint.Warnings.Should().Contain(warning => warning.Contains("core.json"));
        fingerprint.Warnings.Should().Contain(warning => warning.Contains("SPT.Server.deps.json"));
        fingerprint.IsInstall.Should().BeTrue("the game build is readable even without the SPT manifests");
    }

    [Fact]
    public void DetectMany_KeepsOnlyTheFoldersThatAreInstalls()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        using var temp = new TempDirectory();
        var empty = temp.Combine("empty");
        Directory.CreateDirectory(temp.Path);
        Directory.CreateDirectory(empty);

        var found = Detector().DetectMany([install.Root, empty, temp.Path, "/nonexistent/ytt-none"]);

        found.Should().ContainSingle().Which.RootPath.Should().Be(Path.GetFullPath(install.Root));
    }

    /// <summary>A merged install: game, injector and runtime at one root, which is what SPT runs.</summary>
    public static void WriteMergedInstall(TempDirectory temp, string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "EscapeFromTarkov.exe"), FakePe.WithVersion("0.16.9.40743"));
        File.WriteAllText(Path.Combine(root, "UnityPlayer.dll"), "unity");
        Directory.CreateDirectory(Path.Combine(root, "EscapeFromTarkov_Data", "Managed"));
        File.WriteAllText(Path.Combine(root, "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll"), "patched");
        File.WriteAllText(Path.Combine(root, "winhttp.dll"), "doorstop");
        Directory.CreateDirectory(Path.Combine(root, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(root, "BepInEx", "core", "BepInEx.Preloader.dll"), "preloader");

        var runtime = Path.Combine(root, "SPT_Runtime");
        Directory.CreateDirectory(Path.Combine(runtime, "SPT_Data", "configs"));
        File.WriteAllText(Path.Combine(runtime, "SPT.Server.dll"), "server");
        File.WriteAllText(Path.Combine(runtime, "SPT_Data", "configs", "core.json"), """{ "compatibleTarkovVersion": "0.16.9.40743" }""");
        File.WriteAllText(
            Path.Combine(runtime, "SPT.Server.deps.json"),
            """
            { "libraries": { "SPT.Server/4.1.6-RELEASE+abcdef.20260918": { "type": "project" } } }
            """
        );
    }
}
