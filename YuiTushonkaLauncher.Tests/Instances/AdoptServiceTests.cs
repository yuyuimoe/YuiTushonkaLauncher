using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

/// <summary>
/// Adopt is "bring the install that is already here into the launcher": the instance is created in place, and a pristine
/// reference decides which files are game or SPT files and which are modifications.
/// </summary>
public sealed class AdoptServiceTests
{
    [Fact]
    public async Task Plan_ClassifiesAgainstTheReferenceAndKeepsModifications()
    {
        using var world = new AdoptWorld();

        var plan = await world.Service.PlanAsync(world.Request());

        plan.CanAdopt.Should().BeTrue();
        plan.ReferencePath.Should().Be(world.ReferencePath);
        plan.SharedFiles.Should().BeGreaterThan(0);
        plan.ModifiedFiles.Should().Be(1, "the edited assembly differs from the reference");
        plan.ExtraFiles.Should().BeGreaterThan(0, "the mod is not in the reference");
        plan.Modifications.Should().Contain(path => path.EndsWith("BepInEx/plugins/loot/UserMod.dll", StringComparison.Ordinal));
        plan.Modifications.Should().Contain(path => path.Contains("Assembly-CSharp.dll"));
        plan.Install.IsRunnable.Should().BeTrue();
    }

    [Fact]
    public async Task Plan_RecognisesThePatchedFormThroughTheBackup()
    {
        using var world = new AdoptWorld();
        // The instance holds the patched assembly and keeps the pristine one next to it.
        world.Write(world.InstallRoot, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", world.ReferenceAssembly);

        var plan = await world.Service.PlanAsync(world.Request());

        plan.PatchedFiles.Should().Be(1);
        plan.Modifications.Should().NotContain(path => path.EndsWith("Assembly-CSharp.dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Adopt_LinksTheSharedFilesIntoANewBaseAndLeavesTheInstanceWhereItIs()
    {
        using var world = new AdoptWorld(reference: false);

        var instance = await world.Service.AdoptAsync(world.Request());

        instance.Path.Should().Be(world.InstallRoot);
        instance.BaseGamePath.Should().NotBe(world.InstallRoot);
        Directory.Exists(instance.BaseGamePath).Should().BeTrue();
        File.Exists(Path.Combine(world.InstallRoot, "SPT_Runtime/SPT.Server.dll")).Should().BeTrue("adopting never moves the install");

        var linked = Path.Combine(instance.BaseGamePath, "EscapeFromTarkov.exe");
        File.ReadAllBytes(linked).Should().Equal(File.ReadAllBytes(Path.Combine(world.InstallRoot, "EscapeFromTarkov.exe")));
        world.Links.GetLinkCount(linked).Should().Be(2, "the base shares the file with the adopted instance");
    }

    [Fact]
    public async Task Adopt_RecordsVersionsLayoutAndPatchState()
    {
        using var world = new AdoptWorld();

        var instance = await world.Service.AdoptAsync(world.Request());

        instance.SptVersion.ToString().Should().Be("4.1.6");
        instance.TarkovVersion.Should().Be("0.16.9.40743");
        instance.Layout.Should().Be(InstanceLayout.Spt41Plus);
        instance.BaseGamePath.Should().Be(world.ReferencePath);
        instance.Name.Should().Be("my-install");
    }

    [Fact]
    public async Task Adopt_RefusesASplitInstallBecauseSptCannotRunIt()
    {
        using var world = new AdoptWorld();
        using var split = FakeBaseInstall.CreateSpt41();

        var plan = await world.Service.PlanAsync(new AdoptRequest { InstallPath = split.Root, BaseRoot = world.BaseRoot });

        plan.CanAdopt.Should().BeFalse();
        plan.Issues.Should().Contain(issue => issue.Contains("SPT cannot run"));
    }

    [Fact]
    public async Task Adopt_RefusesAFolderThatBelongsToAnotherInstance()
    {
        using var world = new AdoptWorld();
        await world.Service.AdoptAsync(world.Request());

        var again = async () => await world.Service.AdoptAsync(world.Request());

        await again.Should().ThrowAsync<InstanceValidationException>().WithMessage("*already the instance*");
    }

    [Fact]
    public async Task Plan_WarnsWhenThereIsNoReferenceToClassifyAgainst()
    {
        using var world = new AdoptWorld(reference: false);

        var plan = await world.Service.PlanAsync(world.Request());

        plan.CreatesBase.Should().BeTrue();
        plan.ReferencePath.Should().BeNull();
        plan.Warnings.Should().Contain(warning => warning.Contains("treated as a base file"));
        plan.BasePath.Should().EndWith("base-4.1.6-eft-0.16.9.40743");
    }

    /// <summary>A playable install, an optional pristine reference and the service over a real database.</summary>
    private sealed class AdoptWorld : IDisposable
    {
        public AdoptWorld(bool reference = true)
        {
            Temp = new TempDirectory();
            InstallRoot = Path.Combine(Temp.Path, "my-install");
            BaseRoot = Path.Combine(Temp.Path, "bases");
            ReferencePath = Path.Combine(Temp.Path, "reference");
            Directory.CreateDirectory(BaseRoot);
            Links = new FileLinkService(NullLogger<FileLinkService>.Instance);

            WriteInstall(InstallRoot);
            HasReference = reference;
            if (reference)
            {
                WriteReference(ReferencePath);
            }

            Database = new TestDatabase();
            Database.InitializeAsync().GetAwaiter().GetResult();
            Service = new AdoptService(
                new InstallDetector(new PatchStateDetector(NullLogger<PatchStateDetector>.Instance), NullLogger<InstallDetector>.Instance),
                Links,
                Database.UnitOfWork,
                TimeProvider.System,
                NullLogger<AdoptService>.Instance
            );
        }

        public TempDirectory Temp { get; }

        public string InstallRoot { get; }

        public string BaseRoot { get; }

        public string ReferencePath { get; }

        public string ReferenceAssembly { get; } = "the-original-assembly";

        public bool HasReference { get; }

        public FileLinkService Links { get; }

        public TestDatabase Database { get; }

        public AdoptService Service { get; }

        public AdoptRequest Request()
        {
            return new AdoptRequest
            {
                InstallPath = InstallRoot,
                BaseRoot = BaseRoot,
                ReferenceBasePath = HasReference ? ReferencePath : null,
                Name = "my-install",
            };
        }

        public void Write(string root, string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            Database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Temp.Dispose();
        }

        /// <summary>A merged install with one mod and one edited file.</summary>
        private void WriteInstall(string root)
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "EscapeFromTarkov.exe"), FakePe.WithVersion("0.16.9.40743"));
            Write(root, "UnityPlayer.dll", "unity");
            Write(root, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", "patched-assembly");
            Write(root, "EscapeFromTarkov_Data/Resources/bundle.bundle", "bundle");
            Write(root, "winhttp.dll", "doorstop");
            Write(root, "BepInEx/core/BepInEx.Preloader.dll", "preloader");
            Write(root, "BepInEx/plugins/loot/UserMod.dll", "user-mod");
            Write(root, "BepInEx/config/BepInEx.cfg", "[Logging]");
            Write(root, "SPT_Runtime/SPT.Server.dll", "server");
            Write(root, "SPT_Runtime/SPT_Data/configs/core.json", """{ "compatibleTarkovVersion": "0.16.9.40743" }""");
            Write(
                root,
                "SPT_Runtime/SPT.Server.deps.json",
                """
                { "libraries": { "SPT.Server/4.1.6-RELEASE+abcdef.20260918": { "type": "project" } } }
                """
            );
            Write(root, "SPT_Runtime/user/profiles/player.json", "{}");
        }

        /// <summary>The SPT installer's output: split, pristine, same game build.</summary>
        private void WriteReference(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "gamefiles"));
            File.WriteAllBytes(Path.Combine(root, "gamefiles", "EscapeFromTarkov.exe"), FakePe.WithVersion("0.16.9.40743"));
            Write(root, "gamefiles/UnityPlayer.dll", "unity");
            Write(root, "gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", ReferenceAssembly);
            Write(root, "gamefiles/EscapeFromTarkov_Data/Resources/bundle.bundle", "bundle");
            Write(root, "spt/winhttp.dll", "doorstop");
            Write(root, "spt/BepInEx/core/BepInEx.Preloader.dll", "preloader");
            Write(root, "spt/SPT_Runtime/SPT.Server.dll", "server");
            Write(root, "spt/SPT_Runtime/SPT_Data/configs/core.json", """{ "compatibleTarkovVersion": "0.16.9.40743" }""");
            Write(
                root,
                "spt/SPT_Runtime/SPT.Server.deps.json",
                """
                { "libraries": { "SPT.Server/4.1.6-RELEASE+abcdef.20260918": { "type": "project" } } }
                """
            );
        }
    }
}
