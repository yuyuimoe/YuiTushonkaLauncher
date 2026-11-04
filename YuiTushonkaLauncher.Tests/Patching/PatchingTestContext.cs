using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Patching;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Patching;

/// <summary>
/// An instance on disk with a delta set, wired to a throwaway database and every patch service.
/// </summary>
/// <remarks>
/// The tree is built by hand instead of through <c>InstanceService</c>: a patch test needs exact file
/// sizes (the pre-patch guard compares them against the delta header) and nothing else.
/// </remarks>
public sealed class PatchingTestContext : IAsyncDisposable
{
    private readonly IPatchEngine _engine;

    private PatchingTestContext(TestDatabase database, TempDirectory temp, IPatchEngine engine)
    {
        Database = database;
        Temp = temp;
        _engine = engine;
        Root = temp.Combine("instance");
        Writer = new LinkAwareFileWriter(new FileLinkService(NullLogger<FileLinkService>.Instance));

        Service = new PatchingService(
            database.UnitOfWork,
            engine,
            new DeltaSetReader(engine, NullLogger<DeltaSetReader>.Instance),
            new PatchPlanner(),
            new BackupScanner(),
            Writer,
            NullLogger<PatchingService>.Instance
        );
    }

    public TestDatabase Database { get; }

    public TempDirectory Temp { get; }

    /// <summary>The scripted engine; only available on contexts created with <see cref="CreateAsync()" />.</summary>
    public FakePatchEngine Engine
    {
        get { return _engine as FakePatchEngine ?? throw new InvalidOperationException("This context uses the real engine."); }
    }

    /// <summary>The instance root on disk.</summary>
    public string Root { get; }

    public ILinkAwareFileWriter Writer { get; }

    public PatchingService Service { get; }

    public Instance Instance { get; private set; } = null!;

    /// <summary>Instance-relative directory holding the deltas.</summary>
    public string PatchesDirectory
    {
        get { return DeltaSetLocation.Resolve(Root, InstanceLayout.Spt41Plus); }
    }

    /// <summary>Instance-relative path of the binary a delta patches.</summary>
    public const string TargetRelative = "gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll";

    public string TargetPath
    {
        get { return Path.Combine(Root, TargetRelative); }
    }

    public string BackupPath
    {
        get { return TargetPath + DeltaSetLocation.BackupSuffix; }
    }

    public static Task<PatchingTestContext> CreateAsync()
    {
        return CreateAsync(new FakePatchEngine());
    }

    /// <summary>Creates a context that drives the real HDiffPatch engine.</summary>
    public static Task<PatchingTestContext> CreateWithRealEngineAsync()
    {
        return CreateAsync(new SharpHDiffPatchEngine(NullLogger<SharpHDiffPatchEngine>.Instance));
    }

    private static async Task<PatchingTestContext> CreateAsync(IPatchEngine engine)
    {
        var database = new TestDatabase();
        var temp = new TempDirectory();
        await database.InitializeAsync();

        var context = new PatchingTestContext(database, temp, engine);
        context.CreateTree();
        context.Instance = await context.RegisterInstanceAsync();

        return context;
    }

    /// <summary>Writes a delta whose header declares <paramref name="inputSize" /> → <paramref name="outputSize" />.</summary>
    public string AddDelta(long inputSize, long outputSize, string gameRelative = "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")
    {
        var path = Path.Combine(PatchesDirectory, "SPT-core", gameRelative + DeltaSetLocation.DeltaExtension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.WriteAllText(path + FakePatchEngine.HeaderSuffix, $"{inputSize} {outputSize}");

        return path;
    }

    /// <summary>Installs the real fixture delta and the fixture's old file as the target.</summary>
    /// <returns>The bytes the delta must produce.</returns>
    public async Task<byte[]> UseFixtureDeltaAsync()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Patching", "Fixtures");
        var delta = Path.Combine(PatchesDirectory, "SPT-core", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll.delta");

        Directory.CreateDirectory(Path.GetDirectoryName(delta)!);
        File.Copy(Path.Combine(fixtures, "small.delta"), delta);
        Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);
        File.Copy(Path.Combine(fixtures, "old.bin"), TargetPath);

        return await File.ReadAllBytesAsync(Path.Combine(fixtures, "new.bin"));
    }

    /// <summary>Writes the pre-patch build of the target.</summary>
    public void WriteVanilla(int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);
        File.WriteAllBytes(TargetPath, new byte[size]);
    }

    /// <summary>Replaces the target with a file of the given size, without touching the backup.</summary>
    public void WriteTarget(int size)
    {
        File.WriteAllBytes(TargetPath, new byte[size]);
    }

    /// <summary>Reads the persisted <see cref="PatchInfo" /> of the instance.</summary>
    public async Task<PatchInfo> ReadPatchInfoAsync()
    {
        await using var context = Database.CreateContext();
        var row = await context.Instances.AsNoTracking().SingleAsync(instance => instance.Id == Instance.Id);

        return row.Patch;
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        Temp.Dispose();
    }

    private void CreateTree()
    {
        Directory.CreateDirectory(PatchesDirectory);
        Directory.CreateDirectory(Path.Combine(Root, "spt", "SPT_Runtime", "user", "mods"));
        Directory.CreateDirectory(Path.Combine(Root, "gamefiles", "EscapeFromTarkov_Data", "Managed"));
    }

    private async Task<Instance> RegisterInstanceAsync()
    {
        var instance = new Instance
        {
            Id = Guid.NewGuid(),
            Name = "patchtest",
            Path = Root,
            BaseGamePath = Temp.Combine("base"),
            SptVersion = new SemanticVersion(4, 1, 6),
            TarkovVersion = "0.16.9.40743",
            Layout = InstanceLayout.Spt41Plus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await Database.UnitOfWork.ExecuteAsync(
            (context, _) =>
            {
                context.Instances.Add(instance);

                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        return instance;
    }
}
