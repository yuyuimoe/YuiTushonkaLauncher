using Microsoft.Extensions.Logging.Abstractions;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Infrastructure.Mods;
using YuiTushonkaLauncher.Infrastructure.Packs;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Tests.Forge;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Instances;

namespace YuiTushonkaLauncher.Tests.Mods;

/// <summary>
/// A ready-to-use instance plus every mod service, wired against a throwaway SQLite database, a fake
/// Forge client and a real file system.
/// </summary>
public sealed class ModTestContext : IAsyncDisposable
{
    private ModTestContext(
        TestDatabase database,
        TempDirectory locations,
        FakeBaseInstall install,
        AppPaths paths,
        FileLinkService links,
        FakeForgeClient forge
    )
    {
        Database = database;
        Locations = locations;
        Install = install;
        Paths = paths;
        Links = links;
        Forge = forge;

        Instances = new InstanceService(
            database.UnitOfWork,
            links,
            FakeBaseInstall.CreateProbe(),
            new PatchStateDetector(NullLogger<PatchStateDetector>.Instance),
            NullLogger<InstanceService>.Instance
        );

        Installer = new ModInstaller(
            database.UnitOfWork,
            new CopyingDownloader(url => DownloadSources.GetValueOrDefault(url.ToString()) ?? DownloadSource),
            new ArchiveInspector(NullLogger<ArchiveInspector>.Instance),
            new SafeExtractor(NullLogger<SafeExtractor>.Instance),
            new ModIdentityReader(NullLogger<ModIdentityReader>.Instance),
            new LinkAwareFileWriter(links),
            paths,
            NullLogger<ModInstaller>.Instance
        );

        Addons = new AddonService(
            database.UnitOfWork,
            new CopyingDownloader(url => DownloadSources.GetValueOrDefault(url.ToString()) ?? DownloadSource),
            new ArchiveInspector(NullLogger<ArchiveInspector>.Instance),
            new SafeExtractor(NullLogger<SafeExtractor>.Instance),
            new ModIdentityReader(NullLogger<ModIdentityReader>.Instance),
            new LinkAwareFileWriter(links),
            paths,
            NullLogger<AddonService>.Instance
        );

        Integrity = new IntegrityService(database.UnitOfWork, NullLogger<IntegrityService>.Instance);
        Updates = new ModUpdateService(database.UnitOfWork, forge, NullLogger<ModUpdateService>.Instance);

        Migration = new MigrationService(
            database.UnitOfWork,
            FakeBaseInstall.CreateProbe(),
            new PatchStateDetector(NullLogger<PatchStateDetector>.Instance),
            new ModIdentityReader(NullLogger<ModIdentityReader>.Instance),
            forge,
            NullLogger<MigrationService>.Instance
        );

        Resolver = new DependencyResolver(database.UnitOfWork, forge, NullLogger<DependencyResolver>.Instance);

        Packs = new PackService(
            database.UnitOfWork,
            forge,
            Installer,
            Addons,
            new LinkAwareFileWriter(links),
            new PackReader(NullLogger<PackReader>.Instance),
            NullLogger<PackService>.Instance,
            _busy
        );
    }

    /// <summary>What decides whether the pack service may touch the files; a test makes it busy to prove a refusal.</summary>
    private readonly Server.SwitchableBusyGuard _busy = new();

    /// <summary>What decides whether the service may touch the files; setting it changes what the service sees.</summary>
    public Core.Launch.IInstanceBusyGuard Busy
    {
        get { return _busy.Current; }
        set { _busy.Current = value; }
    }

    public TestDatabase Database { get; }

    public TempDirectory Locations { get; }

    public FakeBaseInstall Install { get; }

    public AppPaths Paths { get; }

    public FileLinkService Links { get; }

    public FakeForgeClient Forge { get; }

    public InstanceService Instances { get; }

    public ModInstaller Installer { get; }

    public AddonService Addons { get; }

    public IntegrityService Integrity { get; }

    public ModUpdateService Updates { get; }

    public MigrationService Migration { get; }

    public DependencyResolver Resolver { get; }

    public PackService Packs { get; }

    public static async Task<ModTestContext> CreateAsync(string instanceName = "mods")
    {
        var database = new TestDatabase();
        await database.InitializeAsync();

        var locations = new TempDirectory();
        var install = FakeBaseInstall.CreateSpt41();
        var paths = locations.CreateAppPaths();
        paths.EnsureCreated();

        var context = new ModTestContext(
            database,
            locations,
            install,
            paths,
            new FileLinkService(NullLogger<FileLinkService>.Instance),
            new FakeForgeClient()
        );
        context.Instance = await context.Instances.CreateAsync(
            new InstanceCreationRequest
            {
                Name = instanceName,
                Location = locations.Path,
                BaseInstallPath = install.Root,
            }
        );

        return context;
    }

    public Instance Instance { get; private set; } = null!;

    /// <summary>Archive the fake downloader hands out when no per-URL archive is registered.</summary>
    public string? DownloadSource { get; set; }

    /// <summary>Archives by download URL, for tests that fetch more than one thing.</summary>
    public Dictionary<string, string> DownloadSources { get; } = [];

    /// <summary>Installs a client mod from a prepared archive and returns the result.</summary>
    public Task<ModInstallResult> InstallClientModAsync()
    {
        using var archive = new ZipFixture()
            .AddAssembly("BepInEx/plugins/fixture/FixturePlugin.dll", "ClientMod.dll")
            .Add("BepInEx/config/com.fixture.clientmod.cfg", "[General]");

        return Installer.InstallAsync(new ModInstallRequest { InstanceId = Instance.Id, ArchivePath = archive.Path });
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        Locations.Dispose();
        Install.Dispose();
    }

    /// <summary>Copies a prepared archive into the download directory, mimicking <c>IForgeDownloader</c>.</summary>
    private sealed class CopyingDownloader(Func<Uri, string?> sourceArchiveProvider) : IForgeDownloader
    {
        public async Task<ForgeDownloadResult> DownloadAsync(
            Uri url,
            string destinationDirectory,
            string fileName,
            IProgress<ForgeDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            var sourceArchive = sourceArchiveProvider(url);
            if (sourceArchive is null)
            {
                throw new InvalidOperationException("No source archive was configured for the fake downloader.");
            }

            Directory.CreateDirectory(destinationDirectory);
            var target = Path.Combine(destinationDirectory, fileName);

            var bytes = await File.ReadAllBytesAsync(sourceArchive, cancellationToken);
            await File.WriteAllBytesAsync(target, bytes, cancellationToken);

            return new ForgeDownloadResult
            {
                FilePath = target,
                Length = bytes.Length,
                Sha256 = new string('0', 64),
            };
        }
    }
}
