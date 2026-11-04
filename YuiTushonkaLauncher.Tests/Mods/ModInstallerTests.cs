using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Infrastructure.Mods;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Instances;

namespace YuiTushonkaLauncher.Tests.Mods;

public sealed class ModInstallerTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private TempDirectory _locations = null!;
    private FakeBaseInstall _install = null!;
    private Instance _instance = null!;
    private ModInstaller _installer = null!;
    private IIntegrityService _integrity = null!;
    private AppPaths _paths = null!;

    public async Task InitializeAsync()
    {
        _database = new TestDatabase();
        await _database.InitializeAsync();
        _locations = new TempDirectory();
        _install = FakeBaseInstall.CreateSpt41();

        var links = new FileLinkService(NullLogger<FileLinkService>.Instance);
        _paths = _locations.CreateAppPaths();
        _paths.EnsureCreated();

        var instances = new InstanceService(
            _database.UnitOfWork,
            links,
            FakeBaseInstall.CreateProbe(),
            new PatchStateDetector(NullLogger<PatchStateDetector>.Instance),
            NullLogger<InstanceService>.Instance
        );

        _instance = await instances.CreateAsync(
            new InstanceCreationRequest
            {
                Name = "mods",
                Location = _locations.Path,
                BaseInstallPath = _install.Root,
            }
        );

        _installer = new ModInstaller(
            _database.UnitOfWork,
            new UnusedDownloader(),
            new ArchiveInspector(NullLogger<ArchiveInspector>.Instance),
            new SafeExtractor(NullLogger<SafeExtractor>.Instance),
            new ModIdentityReader(NullLogger<ModIdentityReader>.Instance),
            new LinkAwareFileWriter(links),
            _paths,
            NullLogger<ModInstaller>.Instance
        );

        _integrity = new IntegrityService(_database.UnitOfWork, NullLogger<IntegrityService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        _locations.Dispose();
        _install.Dispose();
    }

    [Fact]
    public async Task InstallAsync_ClientMod_LandsInPluginsAndRecordsHashes()
    {
        using var archive = new ZipFixture()
            .AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll")
            .Add("BepInEx/config/com.fixture.clientmod.cfg", "[General]");

        var result = await _installer.InstallAsync(Request(archive.Path));

        result.Guid.Should().Be("com.fixture.clientmod");
        result.Kind.Should().Be(ModKind.Client);
        result.Version.Should().Be(SemanticVersion.Parse("1.4.2"));
        result.Recognition.Should().Be(ModRecognition.Recognized);
        result.Files.Should().Contain("BepInEx/plugins/fixture/FixturePlugin.dll");
        result.ConfigFiles.Should().Contain("BepInEx/config/com.fixture.clientmod.cfg");

        var installed = Path.Combine(_instance.Path, "BepInEx/plugins/fixture/FixturePlugin.dll");
        File.Exists(installed).Should().BeTrue();

        await using var context = _database.CreateContext();
        var row = await context.InstalledMods.Include(mod => mod.Files).SingleAsync();
        row.Guid.Should().Be("com.fixture.clientmod");
        row.Files.Should().HaveCount(2);
        row.Files.Should().OnlyContain(file => file.Sha256.Length == 64);
    }

    [Fact]
    public async Task InstallAsync_ServerMod_LandsInUserMods()
    {
        using var archive = new ZipFixture().AddAssembly("SPT_Runtime/user/mods/fixture/FixtureServer.dll", "ServerMod.dll");

        var result = await _installer.InstallAsync(Request(archive.Path));

        result.Guid.Should().Be("com.fixture.servermod");
        result.Kind.Should().Be(ModKind.Server);
        result.Files.Should().Contain("SPT_Runtime/user/mods/fixture/FixtureServer.dll");
        File.Exists(Path.Combine(_instance.Path, "SPT_Runtime/user/mods/fixture/FixtureServer.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task InstallAsync_WrappedArchive_IsRelocatedInsideTheInstance()
    {
        using var archive = new ZipFixture().AddAssembly("Fixture-1.0/BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");

        var result = await _installer.InstallAsync(Request(archive.Path));

        result.Files.Should().Contain("BepInEx/plugins/fixture/FixturePlugin.dll");
        result.Files.Should().NotContain(path => path.Contains("Fixture-1.0"));
    }

    [Fact]
    public async Task InstallAsync_WithoutIdentity_FallsBackToUnrecognized()
    {
        using var archive = new ZipFixture().Add("BepInEx/plugins/mystery/readme.txt", "no assembly here");

        var result = await _installer.InstallAsync(Request(archive.Path));

        result.Recognition.Should().Be(ModRecognition.GuidMissing);
        result.Guid.Should().StartWith("unrecognized.");
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InstallAsync_SameVersionTwice_IsANoOp()
    {
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");
        await _installer.InstallAsync(Request(archive.Path));

        var second = await _installer.InstallAsync(Request(archive.Path));

        second.Warnings.Should().Contain(warning => warning.Contains("already installed"));
        await using var context = _database.CreateContext();
        (await context.InstalledMods.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InstallAsync_NewVersion_ReplacesTheOldFiles()
    {
        using var first = new ZipFixture().AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");
        await _installer.InstallAsync(Request(first.Path));

        using var second = new ZipFixture()
            .AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll")
            .Add("BepInEx/plugins/fixture/extra.dll", "extra");
        var result = await _installer.InstallAsync(Request(second.Path));

        result.Warnings.Should().Contain(warning => warning.Contains("Replacing"));
        await using var context = _database.CreateContext();
        var row = await context.InstalledMods.Include(mod => mod.Files).SingleAsync();
        row.Files.Should().HaveCount(2);
    }

    [Fact]
    public async Task InstallAsync_HostileArchive_IsRejectedAndNothingIsWritten()
    {
        using var archive = new ZipFixture().Add("../escape.dll", "evil");

        var act = async () => await _installer.InstallAsync(Request(archive.Path));

        await act.Should().ThrowAsync<InstanceValidationException>();
        File.Exists(Path.Combine(_instance.Path, "..", "escape.dll")).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_RemovesFilesAndRow()
    {
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");
        await _installer.InstallAsync(Request(archive.Path));

        await _installer.UninstallAsync(_instance.Id, "com.fixture.clientmod");

        File.Exists(Path.Combine(_instance.Path, "BepInEx/plugins/fixture/FixturePlugin.dll")).Should().BeFalse();
        await using var context = _database.CreateContext();
        (await context.InstalledMods.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UninstallAsync_RefusesWhenAnotherModRequiresIt()
    {
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");
        await _installer.InstallAsync(Request(archive.Path));

        await using (var context = _database.CreateContext())
        {
            context.Dependencies.Add(
                new Dependency
                {
                    Id = Guid.NewGuid(),
                    OwnerGuid = "com.fixture.clientmod",
                    OwnerKind = PackageKind.Mod,
                    DependencyGuid = "com.fixture.clientmod",
                    Kind = DependencyKind.Required,
                    VersionRange = VersionRange.Parse("^1.0.0"),
                }
            );

            await context.SaveChangesAsync();
        }

        var act = async () => await _installer.UninstallAsync(_instance.Id, "com.fixture.clientmod");
        await act.Should().ThrowAsync<InstanceValidationException>();

        // Forcing is allowed for a user who knows what they are doing.
        await _installer.UninstallAsync(_instance.Id, "com.fixture.clientmod", force: true);
    }

    [Fact]
    public async Task SetPinnedAsync_TogglesTheFlag()
    {
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");
        await _installer.InstallAsync(Request(archive.Path));

        await _installer.SetPinnedAsync(_instance.Id, "com.fixture.clientmod", pinned: true);

        await using var context = _database.CreateContext();
        (await context.InstalledMods.SingleAsync()).IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task Integrity_CleanAfterInstall_FlagsEditsAndMissingFiles()
    {
        using var archive = new ZipFixture()
            .AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll")
            .Add("BepInEx/config/com.fixture.clientmod.cfg", "[General]");
        await _installer.InstallAsync(Request(archive.Path));

        var clean = await _integrity.VerifyAsync(_instance.Id);
        clean.IsClean.Should().BeTrue();

        await File.WriteAllTextAsync(Path.Combine(_instance.Path, "BepInEx/config/com.fixture.clientmod.cfg"), "[Edited]");
        File.Delete(Path.Combine(_instance.Path, "BepInEx/plugins/fixture/FixturePlugin.dll"));

        var report = await _integrity.VerifyAsync(_instance.Id);

        report.IsClean.Should().BeFalse();
        report.Mods.Single().ModifiedConfigs.Should().Contain("BepInEx/config/com.fixture.clientmod.cfg");
        report.DriftedPaths.Should().Contain("BepInEx/plugins/fixture/FixturePlugin.dll");
    }

    [Fact]
    public async Task InstallAsync_UnknownInstance_IsRejected()
    {
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");
        var request = new ModInstallRequest { InstanceId = Guid.NewGuid(), ArchivePath = archive.Path };

        var act = async () => await _installer.InstallAsync(request);

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_MissingArchive_IsRejected()
    {
        var request = new ModInstallRequest { InstanceId = _instance.Id, ArchivePath = "/does/not/exist.zip" };

        var act = async () => await _installer.InstallAsync(request);

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_WithoutArchiveOrUrl_IsRejected()
    {
        var request = new ModInstallRequest { InstanceId = _instance.Id };

        var act = async () => await _installer.InstallAsync(request);

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task InstallAsync_FromADownload_UsesTheDownloaderAndRecordsHashes()
    {
        using var archive = new ZipFixture().AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll");

        var installer = new ModInstaller(
            _database.UnitOfWork,
            new CopyingDownloader(archive.Path),
            new ArchiveInspector(NullLogger<ArchiveInspector>.Instance),
            new SafeExtractor(NullLogger<SafeExtractor>.Instance),
            new ModIdentityReader(NullLogger<ModIdentityReader>.Instance),
            new LinkAwareFileWriter(new FileLinkService(NullLogger<FileLinkService>.Instance)),
            _paths,
            NullLogger<ModInstaller>.Instance
        );

        var result = await installer.InstallAsync(
            new ModInstallRequest { InstanceId = _instance.Id, DownloadUrl = new Uri("https://sp-mod.com/mod/download/31/mod/1.0.0") }
        );

        result.Guid.Should().Be("com.fixture.clientmod");
        result.Warnings.Should().NotContain(warning => warning.Contains("already installed"));
    }

    [Fact]
    public async Task InstallAsync_ExpectedVersion_IsUsedWhenTheAssemblyHasNone()
    {
        using var archive = new ZipFixture().Add("BepInEx/plugins/mystery/readme.txt", "no assembly");

        var result = await _installer.InstallAsync(
            new ModInstallRequest
            {
                InstanceId = _instance.Id,
                ArchivePath = archive.Path,
                ExpectedVersion = Core.Domain.SemanticVersion.Parse("3.1.4"),
            }
        );

        result.Version.Should().Be(Core.Domain.SemanticVersion.Parse("3.1.4"));
    }

    [Fact]
    public async Task UninstallAsync_UnknownMod_IsRejected()
    {
        var act = async () => await _installer.UninstallAsync(_instance.Id, "com.fixture.not-installed");

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task UninstallAsync_UnknownInstance_IsRejected()
    {
        var act = async () => await _installer.UninstallAsync(Guid.NewGuid(), "com.fixture.clientmod");

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task SetPinnedAsync_UnknownMod_IsRejected()
    {
        var act = async () => await _installer.SetPinnedAsync(_instance.Id, "com.fixture.not-installed", pinned: true);

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    private ModInstallRequest Request(string archivePath)
    {
        return new ModInstallRequest { InstanceId = _instance.Id, ArchivePath = archivePath };
    }

    /// <summary>Copies a prepared archive into the download directory, mimicking <c>IForgeDownloader</c>.</summary>
    private sealed class CopyingDownloader(string archivePath) : IForgeDownloader
    {
        public async Task<ForgeDownloadResult> DownloadAsync(
            Uri url,
            string destinationDirectory,
            string fileName,
            IProgress<ForgeDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            Directory.CreateDirectory(destinationDirectory);
            var target = Path.Combine(destinationDirectory, fileName);

            await using var source = File.OpenRead(archivePath);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination, cancellationToken);

            return new ForgeDownloadResult
            {
                FilePath = target,
                Length = new FileInfo(target).Length,
                Sha256 = new string('0', 64),
            };
        }
    }

    /// <summary>The local-archive tests never download; touching this is a bug.</summary>
    private sealed class UnusedDownloader : IForgeDownloader
    {
        public Task<ForgeDownloadResult> DownloadAsync(
            Uri url,
            string destinationDirectory,
            string fileName,
            IProgress<ForgeDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("The installer must not download during these tests.");
        }
    }

    [Fact]
    public async Task InstallAsync_FromADownloadWithoutAnIdentityName_IsNamedByItsGuidNotTheUrlSegment()
    {
        // Found on a real Forge download: the URL ends in the version, and "1.2.1" was recorded as the name.
        await using var context = await ModTestContext.CreateAsync();
        using var archive = new ZipFixture().Add("BepInEx/config/nameless.cfg", "[General]");
        context.DownloadSource = archive.Path;

        var result = await context.Installer.InstallAsync(
            new ModInstallRequest
            {
                InstanceId = context.Instance.Id,
                DownloadUrl = new Uri("https://sp-mod.com/mod/download/7/some-mod/1.4.2"),
                ForgeId = 7,
            }
        );

        await using var verification = context.Database.CreateContext();
        var row = await verification.InstalledMods.SingleAsync(mod => mod.Guid == result.Guid);
        row.Name.Should().Be(result.Guid);
    }
}
