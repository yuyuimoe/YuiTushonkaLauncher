using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class MigrationServiceTests
{
    [Fact]
    public async Task ScanAsync_FindsInstalledModsAndMatchesThemInTheCatalogue()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        context.Forge.WithMod("com.fixture.clientmod", "1.4.2", forgeId: 900, name: "Fixture Client Mod");

        var plan = await context.Migration.ScanAsync(context.Instance.Id);

        plan.Recognized.Should().ContainSingle();
        plan.Recognized[0].Guid.Should().Be("com.fixture.clientmod");
        plan.Recognized[0].ForgeId.Should().Be(900);
        plan.Recognized[0].Name.Should().Be("Fixture Client Mod");
        plan.Recognized[0].Files.Should().Contain(path => path.EndsWith("FixturePlugin.dll", StringComparison.Ordinal));
        plan.TotalMods.Should().Be(1);
    }

    [Fact]
    public async Task ScanAsync_ClassifiesUnknownAndMissingGuids()
    {
        await using var context = await ModTestContext.CreateAsync();

        // A mod folder with a real assembly nobody in the catalogue knows.
        var unknownFolder = Path.Combine(context.Instance.Path, "BepInEx/plugins/unknown", "Unknown.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(unknownFolder)!);
        await File.WriteAllBytesAsync(unknownFolder, await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "ClientMod.dll")));

        // And a folder with no assembly at all.
        var noGuidFile = Path.Combine(context.Instance.Path, "BepInEx/plugins/mystery/readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(noGuidFile)!);
        await File.WriteAllTextAsync(noGuidFile, "no dll here");

        var plan = await context.Migration.ScanAsync(context.Instance.Id);

        plan.Unrecognized.Should().ContainSingle().Which.Guid.Should().Be("com.fixture.clientmod");
        plan.WithoutGuid.Should().ContainSingle().Which.Name.Should().Be("mystery");
        plan.OrphanFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task AdoptAsync_RegistersTheFolderWithoutTouchingItsFiles()
    {
        await using var context = await ModTestContext.CreateAsync();

        // Build a folder that looks like a hand-made install: base layout plus a mod the user dropped in.
        var adoptedRoot = context.Locations.Combine("adopted");
        CopyDirectory(context.Install.Root, adoptedRoot);
        var modFile = Path.Combine(adoptedRoot, "BepInEx/plugins/fixture/FixturePlugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(modFile)!);
        await File.WriteAllBytesAsync(modFile, await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "ClientMod.dll")));

        var before = new FileInfo(modFile).LastWriteTimeUtc;
        context.Forge.WithMod("com.fixture.clientmod", "1.4.2", forgeId: 900);

        var (instance, plan) = await context.Migration.AdoptAsync(adoptedRoot, "adopted");

        instance.Path.Should().Be(Path.GetFullPath(adoptedRoot));
        instance.Layout.Should().Be(InstanceLayout.Spt41Plus);
        plan.Recognized.Should().ContainSingle().Which.Guid.Should().Be("com.fixture.clientmod");
        new FileInfo(modFile).LastWriteTimeUtc.Should().Be(before, "adoption must not modify the user's files");
    }

    [Fact]
    public async Task ApplyAsync_RegistersModsWithHashesAndIsIdempotent()
    {
        await using var context = await ModTestContext.CreateAsync();
        await context.InstallClientModAsync();
        context.Forge.WithMod("com.fixture.clientmod", "1.4.2", forgeId: 900);

        var plan = await context.Migration.ScanAsync(context.Instance.Id);
        var first = await context.Migration.ApplyAsync(plan);
        var second = await context.Migration.ApplyAsync(plan);

        // The mod was installed by the installer, so applying the plan shows it as already registered.
        first.Registered.Should().Be(0);
        first.Skipped.Should().Be(1);
        second.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_RegistersAModThatWasNotManagedYet()
    {
        await using var context = await ModTestContext.CreateAsync();

        var folder = Path.Combine(context.Instance.Path, "BepInEx/plugins/legacy");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(
            Path.Combine(folder, "Legacy.dll"),
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "ClientMod.dll"))
        );
        context.Forge.WithMod("com.fixture.clientmod", "1.4.2", forgeId: 900);

        var plan = await context.Migration.ScanAsync(context.Instance.Id);
        var result = await context.Migration.ApplyAsync(plan);

        result.Registered.Should().Be(1);
        result.FilesHashed.Should().BeGreaterThan(0);

        await using var verification = context.Database.CreateContext();
        var row = await verification.InstalledMods.Include(mod => mod.Files).SingleAsync();
        row.Guid.Should().Be("com.fixture.clientmod");
        row.Source.Should().Be(InstalledModSource.Adopted);
        row.Files.Should().OnlyContain(file => file.Sha256.Length == 64);
    }

    [Fact]
    public async Task ScanAsync_RejectsAnUnknownInstance()
    {
        await using var context = await ModTestContext.CreateAsync();

        var act = async () => await context.Migration.ScanAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
