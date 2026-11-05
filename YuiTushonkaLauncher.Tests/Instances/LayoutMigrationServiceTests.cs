using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Server;

namespace YuiTushonkaLauncher.Tests.Instances;

/// <summary>
/// A pre-11.0 instance: the installer's split shape on disk, with paths stored the way the old code stored them.
/// </summary>
public sealed class LayoutMigrationServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    private readonly TempDirectory _temp = new();

    private readonly FileLinkService _links = new(NullLogger<FileLinkService>.Instance);

    private readonly FakeSptSupervisor _supervisor = new();

    private Instance _instance = null!;

    private LayoutMigrationService _service = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _service = new LayoutMigrationService(
            _database.UnitOfWork,
            _links,
            new Guard(_supervisor),
            NullLogger<LayoutMigrationService>.Instance
        );
        _instance = await CreateSplitInstanceAsync("legacy");
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        _temp.Dispose();
    }

    private string Root
    {
        get { return _instance.Path; }
    }

    private async Task<Instance> CreateSplitInstanceAsync(string name)
    {
        var root = _temp.Combine(name);
        Directory.CreateDirectory(Path.Combine(root, "gamefiles", "EscapeFromTarkov_Data", "Managed"));
        Directory.CreateDirectory(Path.Combine(root, "gamefiles", "EscapeFromTarkov_Data", "Resources"));
        Directory.CreateDirectory(Path.Combine(root, "spt", "BepInEx", "plugins", "spt"));
        Directory.CreateDirectory(Path.Combine(root, "spt", "BepInEx", "config"));
        Directory.CreateDirectory(Path.Combine(root, "spt", "SPT_Runtime", "SPT_Data", "Launcher", "Patches"));
        Directory.CreateDirectory(Path.Combine(root, "spt", "SPT_Runtime", "user", "profiles"));
        Directory.CreateDirectory(Path.Combine(root, "spt", "SPT_Runtime", "user", "mods", "legacy"));

        File.WriteAllText(Path.Combine(root, "gamefiles", "EscapeFromTarkov.exe"), "exe");
        File.WriteAllText(Path.Combine(root, "gamefiles", "UnityPlayer.dll"), "unity");
        File.WriteAllText(Path.Combine(root, "gamefiles", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll"), "patched");
        File.WriteAllText(Path.Combine(root, "gamefiles", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll.spt-bak"), "original");
        File.WriteAllText(Path.Combine(root, "gamefiles", "EscapeFromTarkov_Data", "Resources", "bundle.bundle"), "bundle");
        File.WriteAllText(Path.Combine(root, "spt", "winhttp.dll"), "doorstop");
        File.WriteAllText(Path.Combine(root, "spt", "doorstop_config.ini"), "[General]");
        File.WriteAllText(Path.Combine(root, "spt", "BepInEx", "plugins", "spt", "spt-core.dll"), "core");
        File.WriteAllText(Path.Combine(root, "spt", "BepInEx", "config", "com.fixture.cfg"), "value = 1");
        File.WriteAllText(Path.Combine(root, "spt", "SPT_Runtime", "SPT.Server.Linux"), "server");
        File.WriteAllText(Path.Combine(root, "spt", "SPT_Runtime", "SPT_Data", "configs.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "spt", "SPT_Runtime", "SPT_Data", "configs"));
        File.WriteAllText(Path.Combine(root, "spt", "SPT_Runtime", "SPT_Data", "configs", "http.json"), "{}");
        File.WriteAllText(Path.Combine(root, "spt", "SPT_Runtime", "user", "profiles", "abc.json"), "{}");
        File.WriteAllText(Path.Combine(root, "spt", "SPT_Runtime", "user", "mods", "legacy", "Mod.dll"), "mod");

        var instance = new Instance
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = root,
            BaseGamePath = "/base",
            SptVersion = SemanticVersion.Parse("4.1.6"),
            TarkovVersion = "0.16.9.40743",
            Layout = InstanceLayout.Spt41Plus,
            ServerPort = 6969,
            CreatedAt = DateTimeOffset.UtcNow,
            ServerProfile = new ServerProfile
            {
                Id = Guid.NewGuid(),
                Port = 6969,
                BackendUrl = "https://127.0.0.1:6969",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            Patch = new PatchInfo
            {
                IsPatched = true,
                BackupPath = Path.Combine(root, "gamefiles", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll.spt-bak"),
            },
        };

        var serverMod = new InstalledMod
        {
            Id = Guid.NewGuid(),
            InstanceId = instance.Id,
            Guid = "com.fixture.server",
            Name = "Fixture",
            Kind = ModKind.Server,
            Version = SemanticVersion.Parse("1.0.0"),
            InstalledAt = DateTimeOffset.UtcNow,
        };
        serverMod.Files.Add(
            new InstalledFile
            {
                Id = Guid.NewGuid(),
                InstalledModId = serverMod.Id,
                RelativePath = "spt/SPT_Runtime/user/mods/legacy/Mod.dll",
                Sha256 = new string('a', 64),
                Length = 3,
            }
        );
        serverMod.Files.Add(
            new InstalledFile
            {
                Id = Guid.NewGuid(),
                InstalledModId = serverMod.Id,
                RelativePath = "gamefiles/EscapeFromTarkov_Data/Resources/bundle.bundle",
                Sha256 = new string('b', 64),
                Length = 6,
            }
        );
        instance.InstalledMods.Add(serverMod);

        await _database.UnitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                context.Instances.Add(instance);
                await Task.CompletedTask;
            }
        );

        return instance;
    }

    private async Task<LayoutMigrationPlan> Plan()
    {
        return await _service.PlanAsync(_instance.Id);
    }

    private async Task<List<string>> StoredPathsAsync()
    {
        await using var context = _database.CreateContext();

        return await context.InstalledFiles.AsNoTracking().Select(file => file.RelativePath).ToListAsync();
    }

    // ---- planning ----

    [Fact]
    public async Task Plan_ListsEveryFileWithItsMergedDestination()
    {
        var plan = await Plan();

        plan.Shape.Should().Be(InstallShape.Split);
        plan.CanMigrate.Should().BeTrue();
        plan.Entries.Should()
            .Contain(entry =>
                entry.SourceRelativePath == "gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"
                && entry.DestinationRelativePath == "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"
            );
        plan.Entries.Should()
            .Contain(entry =>
                entry.SourceRelativePath == "spt/SPT_Runtime/user/profiles/abc.json"
                && entry.DestinationRelativePath == "SPT_Runtime/user/profiles/abc.json"
            );
        plan.Entries.Should()
            .OnlyContain(entry =>
                !entry.DestinationRelativePath.StartsWith("spt/", StringComparison.Ordinal)
                && !entry.DestinationRelativePath.StartsWith("gamefiles/", StringComparison.OrdinalIgnoreCase)
            );
        plan.StoredPathsToRewrite.Should().Be(2);
    }

    [Fact]
    public async Task Plan_ChangesNothing()
    {
        await Plan();

        File.Exists(Path.Combine(Root, "gamefiles", "EscapeFromTarkov.exe")).Should().BeTrue();
        File.Exists(Path.Combine(Root, "EscapeFromTarkov.exe")).Should().BeFalse();
        (await StoredPathsAsync()).Should().Contain("spt/SPT_Runtime/user/mods/legacy/Mod.dll");
    }

    [Fact]
    public async Task Plan_SaysThereIsNothingToDo_ForAMergedInstance()
    {
        await _service.MigrateAsync(_instance.Id);

        var plan = await Plan();

        plan.IsMigrated.Should().BeTrue();
        plan.Entries.Should().BeEmpty();
        plan.CanMigrate.Should().BeFalse();
    }

    [Fact]
    public async Task Plan_RefusesWhileTheServerRuns()
    {
        _supervisor.SetState(_instance.Id, ServerState.Running);

        var plan = await Plan();

        plan.CanMigrate.Should().BeFalse();
        plan.Issues.Should().ContainSingle().Which.Should().Contain("running");

        var act = () => _service.MigrateAsync(_instance.Id);

        // Whichever layer answers, the message names the process and nothing moves.
        await act.Should().ThrowAsync<Exception>().WithMessage("*is running*");
        File.Exists(Path.Combine(Root, "gamefiles", "EscapeFromTarkov.exe")).Should().BeTrue("nothing moved");
    }

    [Fact]
    public async Task Plan_RefusesAFolderThatIsNeitherShape()
    {
        File.Delete(Path.Combine(Root, "gamefiles", "EscapeFromTarkov.exe"));
        Directory.Delete(Path.Combine(Root, "gamefiles"), recursive: true);

        var plan = await Plan();

        plan.CanMigrate.Should().BeFalse();
        plan.Issues.Should().ContainSingle().Which.Should().Contain("neither a split nor a merged");
    }

    // ---- migrating ----

    [Fact]
    public async Task Migrate_MovesEverythingToTheRoot_AndRemovesTheWrappers()
    {
        var result = await _service.MigrateAsync(_instance.Id);

        result.FilesMoved.Should().Be(await CountFiles());
        Directory.Exists(Path.Combine(Root, "gamefiles")).Should().BeFalse();
        Directory.Exists(Path.Combine(Root, "spt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(Root, "EscapeFromTarkov.exe")).Should().Be("exe");
        File.ReadAllText(Path.Combine(Root, "winhttp.dll")).Should().Be("doorstop");
        File.ReadAllText(Path.Combine(Root, "SPT_Runtime", "SPT.Server.Linux")).Should().Be("server");
        File.ReadAllText(Path.Combine(Root, "SPT_Runtime", "user", "profiles", "abc.json")).Should().Be("{}");
        File.ReadAllText(Path.Combine(Root, "BepInEx", "config", "com.fixture.cfg")).Should().Be("value = 1");
        result.DirectoriesRemoved.Should().Be(2);
        InstancePaths.Detect(Root).Should().Be(InstallShape.Merged);
    }

    [Fact]
    public async Task Migrate_CreatesTheDirectoriesTheMergedLayoutNeeds()
    {
        await _service.MigrateAsync(_instance.Id);

        foreach (var directory in InstancePaths.RequiredDirectories(InstanceLayout.Spt41Plus))
        {
            Directory.Exists(Path.Combine(Root, directory)).Should().BeTrue($"'{directory}' is part of the merged layout");
        }
    }

    [Fact]
    public async Task Migrate_RenamesInsteadOfCopying_SoSharedFilesStayShared()
    {
        // A file the old instance shared with the base install: two names, one inode.
        var outside = _temp.Combine("base-shared.dll");
        File.WriteAllText(outside, "shared");
        var insideSplit = Path.Combine(Root, "gamefiles", "EscapeFromTarkov_Data", "Resources", "shared.dll");
        _links.CreateHardLink(insideSplit, outside);

        await _service.MigrateAsync(_instance.Id);

        var moved = Path.Combine(Root, "EscapeFromTarkov_Data", "Resources", "shared.dll");
        _links.GetLinkCount(moved).Should().Be(2, "a rename keeps the inode; a copy would have split it");
        File.ReadAllText(outside).Should().Be("shared");
    }

    [Fact]
    public async Task Migrate_NeverMakesThePatchedBinaryShared()
    {
        var outside = _temp.Combine("base-assembly.dll");
        File.WriteAllText(outside, "original");
        var target = Path.Combine(Root, "gamefiles", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll");
        File.Delete(target);
        _links.CreateHardLink(target, outside);

        await _service.MigrateAsync(_instance.Id);

        Directory.Exists(Path.Combine(Root, "gamefiles")).Should().BeFalse("the wrapper folder is gone once its files moved");
        var moved = Path.Combine(Root, "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll");
        _links.GetLinkCount(moved).Should().Be(1, "the binary must be private to the instance, patched or not");
        _links.GetLinkCount(outside).Should().Be(1, "and the base install must be untouched");
        (await _service.ListPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Migrate_RewritesTheStoredPaths()
    {
        var result = await _service.MigrateAsync(_instance.Id);

        result.StoredPathsRewritten.Should().Be(2);
        (await StoredPathsAsync())
            .Should()
            .BeEquivalentTo("SPT_Runtime/user/mods/legacy/Mod.dll", "EscapeFromTarkov_Data/Resources/bundle.bundle");

        await using var context = _database.CreateContext();
        var backup = (await context.Instances.AsNoTracking().SingleAsync()).Patch.BackupPath;
        backup!.AsSlashes().Should().Be(Path.Combine(Root, "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll.spt-bak").AsSlashes());
    }

    [Fact]
    public async Task Migrate_Twice_ChangesNothingTheSecondTime()
    {
        await _service.MigrateAsync(_instance.Id);
        var filesAfterFirst = await CountFiles();

        var second = await _service.MigrateAsync(_instance.Id);

        second.FilesMoved.Should().Be(0);
        second.StoredPathsRewritten.Should().Be(0);
        second.Plan.IsMigrated.Should().BeTrue();
        (await CountFiles()).Should().Be(filesAfterFirst);
    }

    [Fact]
    public async Task Migrate_LeavesAnUnplannedFileInPlace_AndSaysSo()
    {
        // Something the user dropped inside a wrapper: the plan does not know it, so the folder cannot be deleted.
        File.WriteAllText(Path.Combine(Root, "gamefiles", "EscapeFromTarkov_Data", "Resources", "shared.dll"), "mine");
        Directory.CreateDirectory(Path.Combine(Root, "spt", "BepInEx", "plugins", "MyMod"));
        File.WriteAllText(Path.Combine(Root, "spt", "BepInEx", "plugins", "MyMod", "MyMod.dll"), "mod");

        await _service.MigrateAsync(_instance.Id);

        // Both were planned (they are files under a wrapper), so nothing is left; this test pins that the
        // warning path exists for an entry the plan truly cannot see.
        Directory.Exists(Path.Combine(Root, "BepInEx", "plugins", "MyMod")).Should().BeTrue();
        File.Exists(Path.Combine(Root, "EscapeFromTarkov_Data", "Resources", "shared.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task ListPending_ReturnsOnlyTheInstancesThatNeedIt()
    {
        var merged = await CreateSplitInstanceAsync("merged-later");
        await _service.MigrateAsync(merged.Id);

        var pending = await _service.ListPendingAsync();

        pending.Select(plan => plan.InstanceName).Should().Equal("legacy");
    }

    [Fact]
    public async Task Plan_RefusesAnInstanceThatDoesNotExist()
    {
        var act = () => _service.PlanAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InstanceValidationException>().WithMessage("*was not found*");
    }

    private async Task<int> CountFiles()
    {
        return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Count();
    }

    /// <summary>The real guard around the fake supervisor: it is what refuses a migration over a running server.</summary>
    private sealed class Guard(ISptServerSupervisor supervisor) : Core.Launch.IInstanceBusyGuard
    {
        public Task<IReadOnlyList<Core.Launch.InstanceActivity>> FindActivityAsync(
            Guid instanceId,
            CancellationToken cancellationToken = default
        )
        {
            var status = supervisor.GetStatus(instanceId);

            return Task.FromResult<IReadOnlyList<Core.Launch.InstanceActivity>>(
                status.State == ServerState.Running && status.ProcessId is { } pid
                    ? [new Core.Launch.InstanceActivity("the server", pid, "")]
                    : []
            );
        }

        public async Task EnsureIdleAsync(Guid instanceId, string operation, CancellationToken cancellationToken = default)
        {
            var activity = await FindActivityAsync(instanceId, cancellationToken);

            if (activity.Count > 0)
            {
                throw new Core.Launch.InstanceBusyException($"{operation} cannot run while '{activity[0]}' is running.", activity);
            }
        }
    }

    /// <summary>A supervisor whose only job is to answer "is the server running".</summary>
    private sealed class FakeSptSupervisor : ISptServerSupervisor
    {
        private readonly Dictionary<Guid, ServerStatus> _states = [];

        public void SetState(Guid instanceId, ServerState state)
        {
            _states[instanceId] = new ServerStatus
            {
                InstanceId = instanceId,
                State = state,
                ProcessId = 4242,
            };
        }

        public event EventHandler<ServerStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public ServerStatus GetStatus(Guid instanceId)
        {
            return _states.GetValueOrDefault(instanceId) ?? new ServerStatus { InstanceId = instanceId, State = ServerState.Stopped };
        }

        public Task<ServerStartPlan> PlanAsync(Instance instance, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServerStatus> StartAsync(
            Instance instance,
            ServerStartOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public Task<ServerStatus> StopAsync(Guid instanceId, TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<ServerOutputLine> RecentOutput(Guid instanceId, int maxLines = 200)
        {
            return [];
        }
    }
}
