using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Mods;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Instances;
using YuiTushonkaLauncher.Tests.Mods;
using YuiTushonkaLauncher.Tests.Patching;

namespace YuiTushonkaLauncher.Tests.Qa;

/// <summary>
/// The seven invariants from <c>docs/plan/02-domain-model.md</c>, one test each, named after the invariant. Every one is
/// written so it fails if the rule is removed, not merely because the code runs.
/// </summary>
public sealed class InvariantTests
{
    /// <summary>Invariant 1: linking never modifies the base install's inode, mtime or content.</summary>
    [Fact]
    public async Task Invariant1_LinkingNeverTouchesTheBaseInstall()
    {
        await using var context = await ModTestContext.CreateAsync("invariant1");
        var baseFile = Path.Join(context.Install.Root, "gamefiles", "EscapeFromTarkov_Data", "Resources", "bundle.bundle");
        var before = Snapshot(baseFile);
        Directory.CreateDirectory(context.Locations.Combine("instances"));

        var instance = await context.Instances.CreateAsync(
            new InstanceCreationRequest
            {
                Name = "second",
                Location = context.Locations.Combine("instances"),
                BaseInstallPath = context.Install.Root,
            }
        );

        File.Exists(Path.Join(instance.Path, "EscapeFromTarkov_Data", "Resources", "bundle.bundle")).Should().BeTrue();
        Snapshot(baseFile).Should().Be(before, "creating instances must not touch the base install");
    }

    /// <summary>Invariant 2: writing to a linked file detaches it first, leaving the base file untouched.</summary>
    [Fact]
    public async Task Invariant2_WritingToALinkedFileDetachesItFirst()
    {
        using var temp = new TempDirectory();
        var baseFile = System.IO.Path.Join(temp.Path, "base.bin");
        var instanceFile = System.IO.Path.Join(temp.Path, "instance.bin");
        await File.WriteAllTextAsync(baseFile, "vanilla");
        var links = new FileLinkService(NullLogger<FileLinkService>.Instance);
        links.CreateHardLink(instanceFile, baseFile);
        File.ReadAllText(instanceFile).Should().Be("vanilla");

        var writer = new LinkAwareFileWriter(links);
        await writer.WriteAllBytesAsync(instanceFile, "patched"u8.ToArray());

        File.ReadAllText(instanceFile).Should().Be("patched");
        File.ReadAllText(baseFile).Should().Be("vanilla", "the base file must never see an instance's write");
    }

    /// <summary>Invariant 3: `Assembly-CSharp.dll` is never shared, in either layout.</summary>
    [Theory]
    [InlineData("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", InstallShape.Split)]
    [InlineData("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", InstallShape.Merged)]
    [InlineData("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", InstallShape.Split)]
    [InlineData("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", InstallShape.Merged)]
    public void Invariant3_AssemblyCSharpIsNeverHardLinked(string baseRelativePath, InstallShape shape)
    {
        LinkPolicy.Decide(baseRelativePath, isDirectory: false, shape).Should().Be(LinkAction.Copy);
    }

    /// <summary>
    /// Invariant 4: a patch runs only when the file is the build the delta expects. SPT's delta format declares sizes,
    /// not input hashes, so the guard is the declared input size and the verification is the output hash — recorded in
    /// <c>docs/plan/91-before-1.0.md</c> as a deviation from the invariant's wording.
    /// </summary>
    [Fact]
    public async Task Invariant4_APatchRunsOnlyWhenTheInputMatchesTheDelta()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.AddDelta(inputSize: 2048, outputSize: 8192);
        context.WriteVanilla(2048);
        context.WriteTarget(4096);

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Succeeded.Should().BeFalse();
        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Refused);
        context.Engine.Applied.Should().BeEmpty("a file that is not the expected build must not reach the engine");
    }

    /// <summary>Invariant 5: every path extracted from an archive stays inside the destination.</summary>
    [Fact]
    public async Task Invariant5_ExtractionStaysInsideTheDestination()
    {
        using var zip = new ZipFixture().Add("BepInEx/plugins/Safe.dll", "ok").Add("../../escaped.txt", "nope");
        using var temp = new TempDirectory();
        var destination = System.IO.Path.Join(temp.Path, "destination");
        Directory.CreateDirectory(destination);

        var extract = async () => await new SafeExtractor(NullLogger<SafeExtractor>.Instance).ExtractAsync(zip.Path, destination);

        await extract.Should().ThrowAsync<FileLinkException>();
        File.Exists(System.IO.Path.GetFullPath(System.IO.Path.Join(temp.Path, "escaped.txt"))).Should().BeFalse();
    }

    /// <summary>Invariant 6: removing a mod others depend on is refused unless it is explicitly forced.</summary>
    [Fact]
    public async Task Invariant6_RemovalOfADependencyIsRefusedUnlessForced()
    {
        await using var context = await ModTestContext.CreateAsync("invariant6");
        var parent = await context.InstallClientModAsync();
        using var addonArchive = new ZipFixture().Add("Languages/pt-BR.json", "{}");
        var dependent = await context.Addons.InstallAsync(
            new AddonInstallRequest
            {
                InstanceId = context.Instance.Id,
                ParentModGuid = parent.Guid,
                ArchivePath = addonArchive.Path,
                Version = SemanticVersion.Parse("1.0.0"),
                ModVersionConstraint = VersionRange.Any,
            }
        );
        await context.Database.UnitOfWork.ExecuteAsync(
            async (db, token) =>
            {
                db.Dependencies.Add(
                    new Dependency
                    {
                        Id = Guid.NewGuid(),
                        OwnerGuid = dependent.Guid,
                        OwnerKind = PackageKind.Mod,
                        DependencyGuid = parent.Guid,
                        Kind = DependencyKind.Required,
                    }
                );
                await Task.CompletedTask;
            }
        );

        var refused = async () => await context.Installer.UninstallAsync(context.Instance.Id, parent.Guid);
        await refused.Should().ThrowAsync<InstanceValidationException>().WithMessage("*required by*");

        await context.Installer.UninstallAsync(context.Instance.Id, parent.Guid, force: true);
        var remaining = (await context.Instances.GetAsync(context.Instance.Id))!.InstalledMods;
        remaining.Should().NotContain(mod => mod.Guid == parent.Guid);
        remaining
            .Should()
            .Contain(mod => mod.Guid == dependent.Guid, "forcing removes the mod, not the addon that was kept as its own row");
    }

    /// <summary>
    /// Invariant 7: an `Unrecognized` mod is never auto-updated — even when the Forge answers with an update for its
    /// GUID, which is exactly the case that would otherwise install someone else's files.
    /// </summary>
    [Fact]
    public async Task Invariant7_UnrecognizedModsAreNeverUpdatedAutomatically()
    {
        await using var context = await ModTestContext.CreateAsync("invariant7");
        var installed = await context.InstallClientModAsync();
        await context.Database.UnitOfWork.ExecuteAsync(
            async (db, token) =>
            {
                var row = await db.InstalledMods.FindAsync([installed.InstalledModId], token);
                row!.Recognition = ModRecognition.Unrecognized;
                await Task.CompletedTask;
            }
        );

        context.Forge.Report = new ForgeUpdateReport
        {
            SptVersion = new SemanticVersion(4, 1, 6),
            Updates =
            [
                new ForgeUpdate
                {
                    Current = new ForgeUpdateTarget
                    {
                        Guid = installed.Guid,
                        Name = installed.Name,
                        Version = installed.Version,
                    },
                    Recommended = new ForgeUpdateCandidate
                    {
                        VersionId = 2,
                        Version = new SemanticVersion(9, 9, 9),
                        DownloadUrl = "https://sp-mod.com/mod/download/1/x/9.9.9",
                    },
                },
            ],
        };

        var plan = await context.Updates.CheckAsync(context.Instance.Id);

        var status = plan.Statuses.Single(entry => entry.Guid == installed.Guid);
        status.Decision.Should().Be(UpdateDecision.Unrecognized);
        status.AvailableVersion.Should().BeNull("nothing is offered, so nothing can be installed by accident");
    }

    /// <summary>Inode-ish identity plus mtime plus content, in one comparable string.</summary>
    private static string Snapshot(string path)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        return $"{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path).Ticks}|{hash}";
    }
}
