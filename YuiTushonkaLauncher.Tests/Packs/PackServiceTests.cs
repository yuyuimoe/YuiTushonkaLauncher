using System.IO.Compression;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Mods;

namespace YuiTushonkaLauncher.Tests.Packs;

/// <summary>
/// Export and import against real files and a real database. The central test is the acceptance round trip:
/// export an instance with an edited config, rebuild it in a fresh instance, and get the same mods, the same
/// config content, and a clean integrity report.
/// </summary>
public sealed class PackServiceTests : IDisposable
{
    private const string ModGuid = "com.fixture.clientmod";
    private const string ConfigRelative = "spt/BepInEx/config/com.fixture.clientmod.cfg";

    private readonly TempDirectory _temp = new();

    private static ZipFixture ModArchive(string configText = "[General]\nvalue = 1")
    {
        return new ZipFixture()
            .AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll")
            .Add("BepInEx/config/com.fixture.clientmod.cfg", configText);
    }

    /// <summary>Installs the fixture mod from a fake Forge download, so it carries a Forge id.</summary>
    private static async Task InstallFromForgeAsync(ModTestContext context, ZipFixture archive)
    {
        context.Forge.WithMod(ModGuid, "1.4.2", 7);
        context.DownloadSource = archive.Path;
        var version = context.Forge.Mods[ModGuid].Versions[0];

        await context.Installer.InstallAsync(
            new ModInstallRequest
            {
                InstanceId = context.Instance.Id,
                DownloadUrl = new Uri(version.DownloadUrl),
                ForgeId = 7,
                ExpectedVersion = version.Version,
            }
        );
    }

    private static string ConfigPath(ModTestContext context)
    {
        return Path.Combine(context.Instance.Path, ConfigRelative);
    }

    private string NewPackage()
    {
        return _temp.Combine(Guid.NewGuid().ToString("n") + ".sptpack");
    }

    private static async Task<List<(string Guid, string Version, string? Parent)>> InstalledAsync(ModTestContext context)
    {
        await using var db = context.Database.CreateContext();
        var rows = await db.InstalledMods.AsNoTracking().Where(mod => mod.InstanceId == context.Instance.Id).ToListAsync();

        return
        [
            .. rows.OrderBy(mod => mod.Guid, StringComparer.Ordinal).Select(mod => (mod.Guid, mod.Version.ToString(), mod.ParentModGuid)),
        ];
    }

    [Fact]
    public async Task Export_ListsTheModWithItsForgeIdAndNeverIncludesABinary()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");

        var result = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        var mod = result.Manifest.Mods.Should().ContainSingle().Subject;
        mod.Guid.Should().Be(ModGuid);
        mod.ForgeId.Should().Be(7);
        mod.Version.Should().Be("1.4.2");
        mod.ContentSha256.Should().MatchRegex("^[0-9a-f]{64}$");

        using var zip = ZipFile.OpenRead(result.PackagePath);
        zip.Entries.Should()
            .NotContain(entry => PackRules.IsForbiddenExtension(entry.FullName), "a mod DLL must never end up in a package");
        zip.Entries.Select(entry => entry.FullName).Should().BeEquivalentTo("manifest.json", "README.txt", "configs/" + ConfigRelative);
    }

    [Fact]
    public async Task Export_OnlyIncludesConfigsThatWereEdited()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);

        var untouched = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var edited = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        untouched.Manifest.Configs.Should().BeEmpty();
        var config = edited.Manifest.Configs.Should().ContainSingle().Subject;
        config.Path.Should().Be(ConfigRelative);
        config.ModGuid.Should().Be(ModGuid);
        config.OriginalSha256.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Export_WithoutConfigs_OrWithAnExclusion_LeavesThemOutAndSaysWhy()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");

        var withoutAll = await source.Packs.ExportAsync(source.Instance.Id, NewPackage(), new ExportOptions { IncludeConfigs = false });
        var excluded = await source.Packs.ExportAsync(
            source.Instance.Id,
            NewPackage(),
            new ExportOptions { ExcludedConfigs = [ConfigRelative] }
        );

        withoutAll.Manifest.Configs.Should().BeEmpty();
        excluded.Manifest.Configs.Should().BeEmpty();
        excluded.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("your request");
    }

    [Fact]
    public async Task Export_SkipsAnEditedConfigThatIsTooLarge()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), new string('x', (int)PackRules.MaxConfigBytes + 10));

        var result = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        result.Manifest.Configs.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("limit");
    }

    [Fact]
    public async Task Export_AnEditedTextFile_IsSharedEvenThoughTheInstallerDoesNotCallItAConfig()
    {
        // Found on the real SVM install: its MOTD.txt is user-edited, but ".txt" was never flagged IsConfig,
        // so the first version of the export silently dropped the edit.
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive().Add("BepInEx/plugins/fixture/motd.txt", "hello");
        await InstallFromForgeAsync(source, archive);
        const string motd = "spt/BepInEx/plugins/fixture/motd.txt";
        File.WriteAllText(Path.Combine(source.Instance.Path, motd), "hello from yui");

        var result = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        result.Manifest.Configs.Should().ContainSingle().Which.Path.Should().Be(motd);
    }

    [Fact]
    public async Task Export_AnEditedBinary_IsReportedAsSkippedInsteadOfVanishing()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        const string dll = "spt/BepInEx/plugins/fixture/FixturePlugin.dll";
        await File.AppendAllTextAsync(Path.Combine(source.Instance.Path, dll), "patched by hand");

        var result = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        result.Manifest.Configs.Should().BeEmpty();
        var skipped = result.Skipped.Should().ContainSingle().Subject;
        skipped.Path.Should().Be(dll);
        skipped.Reason.Should().Contain("not shareable");
    }

    [Fact]
    public async Task RoundTrip_OfATextFileConfig_ReproducesTheSameShareCode()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive().Add("BepInEx/plugins/fixture/motd.txt", "hello");
        await InstallFromForgeAsync(source, archive);
        const string motd = "spt/BepInEx/plugins/fixture/motd.txt";
        File.WriteAllText(Path.Combine(source.Instance.Path, motd), "hello from yui");
        var package = NewPackage();
        var exported = await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        fresh.DownloadSource = archive.Path;
        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);

        result.Succeeded.Should().BeTrue(result.Summary);
        File.ReadAllText(Path.Combine(fresh.Instance.Path, motd)).Should().Be("hello from yui");
        (await fresh.Integrity.VerifyAsync(fresh.Instance.Id)).IsClean.Should().BeTrue();
        (await fresh.Packs.ExportAsync(fresh.Instance.Id, NewPackage())).ShareCode.Should().Be(exported.ShareCode);
    }

    [Fact]
    public async Task Export_TwiceOfAnUnchangedInstance_GivesTheSameShareCode()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");

        var first = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());
        await Task.Delay(1100);
        var second = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        second.Manifest.CreatedAt.Should().BeAfter(first.Manifest.CreatedAt);
        second.ShareCode.Should().Be(first.ShareCode);
    }

    [Fact]
    public async Task Export_ChangingAConfigChangesTheShareCode()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var first = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 43");
        var second = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        second.ShareCode.Should().NotBe(first.ShareCode);
    }

    [Fact]
    public async Task Export_AModThatNeverCameFromTheForge_IsListedWithAWarning()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        await source.InstallClientModAsync();

        var result = await source.Packs.ExportAsync(source.Instance.Id, NewPackage());

        result.Manifest.Mods.Should().ContainSingle().Which.ForgeId.Should().BeNull();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("did not come from the Forge");
    }

    [Fact]
    public async Task RoundTrip_RebuildsTheSameModsAndConfigsInAFreshInstance()
    {
        // The acceptance criterion: export with one edited config -> import into a fresh instance ->
        // identical mod set, identical config content, integrity clean.
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var package = NewPackage();
        var exported = await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        fresh.DownloadSource = archive.Path;

        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);

        result.Succeeded.Should().BeTrue(result.Summary);
        (await InstalledAsync(fresh)).Should().Equal(await InstalledAsync(source));
        File.ReadAllText(ConfigPath(fresh)).Should().Be("[General]\nvalue = 42");

        var integrity = await fresh.Integrity.VerifyAsync(fresh.Instance.Id);
        integrity.IsClean.Should().BeTrue(string.Join(", ", integrity.DriftedPaths));

        result.Items.Should().Contain(item => item.Kind == ImportItemKind.Mod && item.Outcome == ImportOutcome.Installed && item.Verified);
        result
            .Items.Should()
            .Contain(item => item.Kind == ImportItemKind.Config && item.Outcome == ImportOutcome.ConfigApplied && item.Verified);

        // Re-exporting what was just imported reproduces the package: the baseline moved, the shipped hash did not.
        var again = await fresh.Packs.ExportAsync(fresh.Instance.Id, NewPackage());
        again.ShareCode.Should().Be(exported.ShareCode);
    }

    [Fact]
    public async Task Plan_ShowsWhatWouldHappen_AndWritesNothing()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.HasChanges.Should().BeTrue();
        var mod = plan.Items.Single(item => item.Kind == ImportItemKind.Mod);
        mod.Action.Should().Be(ImportAction.Install);
        mod.DownloadUrl.Should().Be("https://sp-mod.com/mod/download/7/x/1.4.2", "the URL comes from the Forge, never from the package");
        plan.Items.Single(item => item.Kind == ImportItemKind.Config).Action.Should().Be(ImportAction.ApplyConfig);
        (await InstalledAsync(fresh)).Should().BeEmpty();
        File.Exists(ConfigPath(fresh)).Should().BeFalse();
    }

    [Fact]
    public async Task Plan_ForAnInstanceThatAlreadyMatches_HasNothingToDo()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        var plan = await source.Packs.PlanImportAsync(source.Instance.Id, package);

        plan.HasChanges.Should().BeFalse();
        plan.Items.Single(item => item.Kind == ImportItemKind.Mod).Action.Should().Be(ImportAction.AlreadyInstalled);
        plan.Items.Single(item => item.Kind == ImportItemKind.Config).Action.Should().Be(ImportAction.ConfigUpToDate);
    }

    [Fact]
    public async Task Import_AModThatIsNotOnTheForge_IsReportedUnavailableAndNothingIsInstalled()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        await source.InstallClientModAsync();
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);
        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);

        plan.UnavailableCount.Should().Be(2, "the mod and the config that belongs to it");
        plan.Items.Single(item => item.Kind == ImportItemKind.Mod).Reason.Should().Contain("install it by hand");
        result.Succeeded.Should().BeFalse();
        (await InstalledAsync(fresh)).Should().BeEmpty();
        File.Exists(ConfigPath(fresh)).Should().BeFalse();
    }

    [Fact]
    public async Task Plan_AVersionThatLeftTheForge_IsUnavailable()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "9.9.9", 7);

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Items.Single().Reason.Should().Contain("no longer on the Forge");
    }

    [Fact]
    public async Task Plan_WhenTheForgeIsUnreachable_ExplainsInsteadOfThrowing()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.Unreachable = true;

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Items.Single().Action.Should().Be(ImportAction.Unavailable);
        plan.Items.Single().Reason.Should().Contain("could not be reached");
    }

    [Fact]
    public async Task Plan_ADownloadAddressThatIsNotHttps_IsNotTrusted()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        var mod = fresh.Forge.Mods[ModGuid];
        fresh.Forge.Mods[ModGuid] = mod with { Versions = [mod.Versions[0] with { DownloadUrl = "http://evil.example/mod.zip" }] };

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Items.Single().Action.Should().Be(ImportAction.Unavailable);
        plan.Items.Single().Reason.Should().Contain("not https");
    }

    [Fact]
    public async Task Plan_WarnsWhenThePackageWasMadeForAnotherSptVersion()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(PackCrafter.Manifest(sptVersion: "3.9.0")));

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Warnings.Should().Contain(warning => warning.Contains("3.9.0") && warning.Contains("4.1"));
    }

    [Fact]
    public async Task Plan_ADifferentInstalledVersion_IsAReplaceThatNeedsPermission()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        await fresh.InstallClientModAsync();
        fresh.Forge.WithMod(ModGuid, "1.5.0", 7);
        fresh.DownloadSource = ModArchive().Path;
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(PackCrafter.Manifest([PackCrafter.Mod(version: "1.5.0")])));

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);
        var withoutPermission = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);
        var withPermission = await fresh.Packs.ImportAsync(
            fresh.Instance.Id,
            package,
            new ImportOptions { ReplaceDifferentVersions = true }
        );

        plan.Items.Single().Action.Should().Be(ImportAction.Replace);
        withoutPermission.Items.Single().Outcome.Should().Be(ImportOutcome.Skipped);
        withPermission.Items.Single().Outcome.Should().Be(ImportOutcome.Replaced);
    }

    [Fact]
    public async Task Import_KeepsAConfigTheUserEdited_UnlessToldToOverwriteIt()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        await InstallFromForgeAsync(fresh, archive);
        File.WriteAllText(ConfigPath(fresh), "[General]\nvalue = MINE");

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);
        var kept = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);
        File.ReadAllText(ConfigPath(fresh)).Should().Be("[General]\nvalue = MINE");

        var overwritten = await fresh.Packs.ImportAsync(fresh.Instance.Id, package, new ImportOptions { OverwriteEditedConfigs = true });

        plan.Items.Single(item => item.Kind == ImportItemKind.Config).Action.Should().Be(ImportAction.KeepLocalConfig);
        kept.Items.Single(item => item.Kind == ImportItemKind.Config).Outcome.Should().Be(ImportOutcome.Skipped);
        overwritten.Items.Single(item => item.Kind == ImportItemKind.Config).Outcome.Should().Be(ImportOutcome.ConfigApplied);
        File.ReadAllText(ConfigPath(fresh)).Should().Be("[General]\nvalue = 42");
        (await fresh.Integrity.VerifyAsync(fresh.Instance.Id)).IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Import_CanSkipConfigsEntirely()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        File.WriteAllText(ConfigPath(source), "[General]\nvalue = 42");
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        fresh.DownloadSource = archive.Path;

        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package, new ImportOptions { ApplyConfigs = false });

        result.Items.Single(item => item.Kind == ImportItemKind.Config).Outcome.Should().Be(ImportOutcome.Skipped);
        File.ReadAllText(ConfigPath(fresh)).Should().Be("[General]\nvalue = 1", "the mod's own shipped config stays");
    }

    [Fact]
    public async Task Import_RemovesAFreshInstallWhoseDownloadedContentIsNotWhatTheExporterHad()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        // The host now serves the same version with an extra file inside.
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        using var swapped = ModArchive().Add("BepInEx/plugins/fixture/injected.md", "not in the original");
        fresh.DownloadSource = swapped.Path;

        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);

        result.Succeeded.Should().BeFalse();
        result.Items.Single().Message.Should().Contain("not what the package was made from").And.Contain("removed again");
        (await InstalledAsync(fresh)).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_KeepsChangedContentWhenTheUserAcceptsIt()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        var package = NewPackage();
        await source.Packs.ExportAsync(source.Instance.Id, package);

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        using var swapped = ModArchive().Add("BepInEx/plugins/fixture/injected.md", "not in the original");
        fresh.DownloadSource = swapped.Path;

        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package, new ImportOptions { AcceptChangedContent = true });

        result.Succeeded.Should().BeTrue();
        (await InstalledAsync(fresh)).Should().ContainSingle();
    }

    [Fact]
    public async Task Import_NeverWritesAConfigThatNoInstalledModOwns()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7);
        fresh.DownloadSource = ModArchive().Path;
        const string foreign = "spt/BepInEx/config/not-a-file-of-the-mod.cfg";
        var manifest = PackCrafter.Manifest(configs: [PackCrafter.Config(ModGuid, foreign, "planted")]);
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(manifest), PackCrafter.ConfigEntry(foreign, "planted"));

        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);

        var config = result.Items.Single(item => item.Kind == ImportItemKind.Config);
        config.Outcome.Should().Be(ImportOutcome.Failed);
        config.Message.Should().Contain("not a config file of");
        File.Exists(Path.Combine(fresh.Instance.Path, foreign)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_ATamperedPackage_IsRefusedBeforeAnythingHappens()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        var manifest = PackCrafter.Manifest();
        var edited = manifest with { Mods = [PackCrafter.Mod(version: "9.9.9")] };
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(edited with { ShareCode = manifest.ShareCode }));

        var plan = async () => await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);
        var import = async () => await fresh.Packs.ImportAsync(fresh.Instance.Id, package);

        (await plan.Should().ThrowAsync<PackException>()).Which.Problem.Should().Be(PackProblem.Tampered);
        (await import.Should().ThrowAsync<PackException>()).Which.Problem.Should().Be(PackProblem.Tampered);
        (await InstalledAsync(fresh)).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ANewerFormat_FailsWithAnUpgradeHint()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        var package = PackCrafter.Zip(
            NewPackage(),
            new PackCrafter.Entry("manifest.json", System.Text.Encoding.UTF8.GetBytes("{\"formatVersion\":2}"))
        );

        var act = async () => await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        (await act.Should().ThrowAsync<PackException>()).Which.Message.Should().Contain("Update the launcher");
    }

    [Fact]
    public async Task Export_ForAnUnknownInstance_Throws()
    {
        await using var source = await ModTestContext.CreateAsync("source");

        var act = async () => await source.Packs.ExportAsync(Guid.NewGuid(), NewPackage());

        await act.Should().ThrowAsync<YuiTushonkaLauncher.Core.Instances.InstanceValidationException>();
    }

    [Fact]
    public async Task RoundTrip_CarriesAnAddonAlongWithItsMod()
    {
        await using var source = await ModTestContext.CreateAsync("source");
        using var archive = ModArchive();
        await InstallFromForgeAsync(source, archive);
        using var addonArchive = new ZipFixture().Add("Languages/pt-BR.json", "{}");
        source.Forge.WithAddon(99, "1.0.0");
        var addonUrl = source.Forge.AddonVersions[99][0].DownloadUrl;
        source.DownloadSources[addonUrl] = addonArchive.Path;

        await source.Addons.InstallAsync(
            new AddonInstallRequest
            {
                InstanceId = source.Instance.Id,
                ParentModGuid = ModGuid,
                ForgeId = 99,
                DownloadUrl = new Uri(addonUrl),
                Version = YuiTushonkaLauncher.Core.Domain.SemanticVersion.Parse("1.0.0"),
            }
        );

        var package = NewPackage();
        var exported = await source.Packs.ExportAsync(source.Instance.Id, package);

        var addon = exported.Manifest.Addons.Should().ContainSingle().Subject;
        addon.ParentGuid.Should().Be(ModGuid);
        addon.ForgeId.Should().Be(99, "the addon's Forge id is recorded now, so it can be fetched again");

        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7).WithAddon(99, "1.0.0");
        fresh.DownloadSource = archive.Path;
        fresh.DownloadSources[addonUrl] = addonArchive.Path;

        var result = await fresh.Packs.ImportAsync(fresh.Instance.Id, package);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Items.Select(item => item.Message)));
        (await InstalledAsync(fresh)).Should().Equal(await InstalledAsync(source));
        (await InstalledAsync(fresh)).Should().Contain(row => row.Parent == ModGuid);
    }

    [Fact]
    public async Task Plan_AnAddonThatDoesNotSupportThePackagesModVersion_IsUnavailable()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2", 7).WithAddon(99, "1.0.0", ">=9.0.0");
        var addon = new PackAddon
        {
            ParentGuid = ModGuid,
            Guid = "addon.x",
            ForgeId = 99,
            Name = "x",
            Version = "1.0.0",
        };
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(PackCrafter.Manifest(addons: [addon])));

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Items.Single(item => item.Kind == ImportItemKind.Addon).Reason.Should().Contain("supports mod versions");
    }

    [Fact]
    public async Task Plan_AnAddonWhoseModCannotBeInstalled_IsUnavailable()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        var addon = new PackAddon
        {
            ParentGuid = ModGuid,
            Guid = "addon.x",
            ForgeId = 99,
            Name = "x",
            Version = "1.0.0",
        };
        var manifest = PackCrafter.Manifest([PackCrafter.Mod(forgeId: null)], addons: [addon]);
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(manifest));

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Items.Single(item => item.Kind == ImportItemKind.Addon).Reason.Should().Contain("cannot be installed");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public async Task Plan_PrefersTheExactPublishedVersion_WhenTwoReleasesDifferOnlyInBuildMetadata()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2+a", 7);
        var mod = fresh.Forge.Mods[ModGuid];
        var first = mod.Versions[0];
        fresh.Forge.Mods[ModGuid] = mod with
        {
            Versions =
            [
                first,
                first with
                {
                    Id = 99,
                    Version = YuiTushonkaLauncher.Core.Domain.SemanticVersion.Parse("1.4.2+b"),
                    DownloadUrl = "https://sp-mod.com/mod/download/7/x/1.4.2+b",
                },
            ],
        };
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(PackCrafter.Manifest([PackCrafter.Mod(version: "1.4.2+b")])));

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Items.Single().DownloadUrl.Should().EndWith("1.4.2+b");
    }

    [Fact]
    public async Task Plan_FindsTheVersionByPrecedence_WhenOnlyTheBuildMetadataDiffers()
    {
        await using var fresh = await ModTestContext.CreateAsync("fresh");
        fresh.Forge.WithMod(ModGuid, "1.4.2+h4", 7);
        var package = PackCrafter.Zip(NewPackage(), PackCrafter.ManifestEntry(PackCrafter.Manifest([PackCrafter.Mod(version: "1.4.2")])));

        var plan = await fresh.Packs.PlanImportAsync(fresh.Instance.Id, package);

        plan.Items.Single().Action.Should().Be(ImportAction.Install);
    }
}
