using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Mods;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerSyncServiceTests : IAsyncLifetime
{
    private const string ServerGuid = "com.fixture.servermod";

    private const string ClientGuid = "com.fixture.clientmod";

    private readonly FakeSptClient _server = new();

    private readonly RecordingInstaller _installer = new();

    private ModTestContext _context = null!;

    private ServerSyncService _service = null!;

    private FakeClientFactory _clients = null!;

    public async Task InitializeAsync()
    {
        _context = await ModTestContext.CreateAsync("sync");
        _installer.Inner = _context.Installer;
        _clients = new FakeClientFactory(_server);

        _service = new ServerSyncService(
            _context.Database.UnitOfWork,
            new SptServerLocator(new FakePlatform(linux: true)),
            _clients,
            _installer,
            _context.Forge
        );
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private async Task InstallServerModAsync()
    {
        using var archive = new ZipFixture().AddAssembly("SPT_Runtime/user/mods/fixture/FixtureServer.dll", "ServerMod.dll");
        await _context.Installer.InstallAsync(new ModInstallRequest { InstanceId = _context.Instance.Id, ArchivePath = archive.Path });
    }

    private Task<ModInstallResult> InstallClientModAsync()
    {
        return _context.InstallClientModAsync();
    }

    private void ServerRuns(string version, string guid = ServerGuid, string name = "Fixture Server Mod")
    {
        _server.ServerMods.Add(
            new SptServerMod
            {
                ModGuid = guid,
                Name = name,
                Version = version,
            }
        );
    }

    private void ForgeHas(string version, int forgeId = 7, string guid = ServerGuid)
    {
        _context.Forge.WithMod(guid, version, forgeId);
        using var archive = new ZipFixture().AddAssembly("SPT_Runtime/user/mods/fixture/FixtureServer.dll", "ServerMod.dll");
        var path = Path.Combine(_context.Locations.Path, $"forge-{guid}-{version}.zip");
        File.Copy(archive.Path, path, overwrite: true);
        _context.DownloadSource = path;
    }

    private async Task<ServerSyncPlan> PlanAsync(ServerEndpoint? endpoint = null)
    {
        return await _service.PlanAsync(_context.Instance.Id, endpoint);
    }

    // ---- planning ----

    [Fact]
    public async Task Plan_SaysMissing_ForAServerModTheInstanceLacks()
    {
        ServerRuns("2.0.1");

        var plan = await PlanAsync();

        plan.ServerMods.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new ServerSyncEntry
                {
                    Guid = ServerGuid,
                    Name = "Fixture Server Mod",
                    ServerVersion = "2.0.1",
                    Status = ServerSyncStatus.Missing,
                }
            );
        plan.IsInSync.Should().BeFalse();
        plan.Actionable.Should().HaveCount(1);
    }

    [Fact]
    public async Task Plan_SaysInSync_WhenTheVersionsMatch()
    {
        await InstallServerModAsync();
        ServerRuns("2.0.1");

        var plan = await PlanAsync();

        plan.ServerMods.Should().ContainSingle().Which.Status.Should().Be(ServerSyncStatus.InSync);
        plan.IsInSync.Should().BeTrue();
        plan.Actionable.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_IgnoresBuildMetadata_WhenComparingVersions()
    {
        await InstallServerModAsync();
        ServerRuns("2.0.1+h4");

        (await PlanAsync()).ServerMods.Single().Status.Should().Be(ServerSyncStatus.InSync);
    }

    [Fact]
    public async Task Plan_SaysVersionDiffers_WhenTheServerRunsAnotherVersion()
    {
        await InstallServerModAsync();
        ServerRuns("2.1.0");

        var entry = (await PlanAsync()).ServerMods.Single();

        entry.Status.Should().Be(ServerSyncStatus.VersionDiffers);
        entry.ServerVersion.Should().Be("2.1.0");
        entry.LocalVersion.Should().Be("2.0.1");
    }

    [Fact]
    public async Task Plan_SaysVersionDiffers_WhenTheServerVersionIsNotAVersionAtAll()
    {
        await InstallServerModAsync();
        ServerRuns("banana");

        (await PlanAsync()).ServerMods.Single().Status.Should().Be(ServerSyncStatus.VersionDiffers);
    }

    [Fact]
    public async Task Plan_RespectsAPin_AndSaysSo()
    {
        await InstallServerModAsync();
        await _context.Installer.SetPinnedAsync(_context.Instance.Id, ServerGuid, pinned: true);
        ServerRuns("2.1.0");

        var plan = await PlanAsync();

        plan.ServerMods.Single().Status.Should().Be(ServerSyncStatus.PinnedDiffers);
        plan.ServerMods.Single().Detail.Should().Contain("pinned");
        plan.Actionable.Should().BeEmpty("a pinned mod is never part of what gets applied by default");
    }

    [Fact]
    public async Task Plan_ListsAModOnlyTheInstanceHas_AsLocalOnly_AndCallsItInSync()
    {
        await InstallServerModAsync();

        var plan = await PlanAsync();

        plan.ServerMods.Should().ContainSingle().Which.Status.Should().Be(ServerSyncStatus.LocalOnly);
        plan.IsInSync.Should().BeTrue("an extra local mod is not something the server needs");
    }

    [Fact]
    public async Task Plan_MatchesGuidsIgnoringCase()
    {
        await InstallServerModAsync();
        ServerRuns("2.0.1", guid: ServerGuid.ToUpperInvariant());

        (await PlanAsync()).ServerMods.Single().Status.Should().Be(ServerSyncStatus.InSync);
    }

    [Fact]
    public async Task Plan_KeepsServerModsAndClientModsApart()
    {
        await InstallClientModAsync();
        ServerRuns("2.0.1");

        var plan = await PlanAsync();

        plan.ServerMods.Should().ContainSingle(entry => entry.Guid == ServerGuid && entry.Status == ServerSyncStatus.Missing);
        plan.ServerMods.Should().NotContain(entry => entry.Guid == ClientGuid, "a client mod is not a server mod");
    }

    [Fact]
    public async Task Plan_AsksTheServerGiven_NotTheInstancesOwn()
    {
        ServerRuns("2.0.1");
        var remote = new ServerEndpoint("203.0.113.9", 7000);

        var plan = await PlanAsync(remote);

        plan.Server.Should().Be(remote);
        _clients.Created.Should().ContainSingle().Which.Should().Be(remote);
    }

    [Fact]
    public async Task Plan_ReportsTheServersSptVersion()
    {
        _server.Version = "4.1.6";

        (await PlanAsync()).ServerSptVersion.Should().Be("4.1.6");
    }

    [Fact]
    public async Task Plan_SaysNoServerAnswers_WhenItDoesNot()
    {
        _server.Up = false;

        var act = () => PlanAsync();

        (await act.Should().ThrowAsync<ServerNotRunningException>()).Which.Message.Should().Contain("No SPT server answers");
    }

    // ---- client mods ----

    private async Task SelectProfileWithRecordedMods(params SptProfileMod[] mods)
    {
        var profile = FakeSptClient.NewProfile("yui", "Standard", "profile-1") with { PendingWipe = false, ClientMods = mods };
        _server.Profiles.Add(profile);

        await _context.Database.UnitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                (await context.ServerProfiles.SingleAsync(token)).LastProfileId = "profile-1";
            }
        );
    }

    [Fact]
    public async Task ClientMods_AreEmpty_WhenNoProfileWasEverSelected()
    {
        await InstallClientModAsync();

        (await PlanAsync()).ClientMods.Should().BeEmpty();
    }

    [Fact]
    public async Task ClientMods_AreEmpty_WhenTheServerRecordedNothingYet_NotAWallOfLocalOnly()
    {
        await InstallClientModAsync();
        await SelectProfileWithRecordedMods();

        (await PlanAsync()).ClientMods.Should().BeEmpty("the server learns client mods from the game, so a profile never played has none");
    }

    [Fact]
    public async Task ClientMods_MatchByName_AndFlagWhatDiffers()
    {
        var installed = await InstallClientModAsync();
        await SelectProfileWithRecordedMods(
            new SptProfileMod(installed.Name, installed.Version.ToString(), null),
            new SptProfileMod("Gone Mod", "1.0.0", null)
        );

        var mods = (await PlanAsync()).ClientMods;

        mods.Should().ContainSingle(entry => entry.Name == installed.Name && entry.Status == ServerSyncStatus.InSync);
        mods.Should()
            .ContainSingle(entry =>
                entry.Name == "Gone Mod" && entry.Status == ServerSyncStatus.Missing && entry.RecordedVersion == "1.0.0"
            );
    }

    [Fact]
    public async Task ClientMods_SayVersionDiffers_WhenTheRecordedVersionIsNotTheInstalledOne()
    {
        var installed = await InstallClientModAsync();
        await SelectProfileWithRecordedMods(new SptProfileMod(installed.Name, "9.9.9", null));

        var entry = (await PlanAsync()).ClientMods.Single();

        entry.Status.Should().Be(ServerSyncStatus.VersionDiffers);
        entry.RecordedVersion.Should().Be("9.9.9");
        entry.LocalVersion.Should().Be(installed.Version.ToString());
    }

    [Fact]
    public async Task ClientMods_ListAnInstalledModTheServerNeverSaw_AsLocalOnly_OnceTheServerRecordedSomething()
    {
        var installed = await InstallClientModAsync();
        await SelectProfileWithRecordedMods(new SptProfileMod("Some Other Mod", null, null));

        var mods = (await PlanAsync()).ClientMods;

        mods.Should().ContainSingle(entry => entry.Name == installed.Name && entry.Status == ServerSyncStatus.LocalOnly);
    }

    [Fact]
    public async Task ClientMods_MatchByGuid_WhenTheServerRecordedTheGuid()
    {
        var installed = await InstallClientModAsync();
        await SelectProfileWithRecordedMods(new SptProfileMod(ClientGuid, null, null));

        (await PlanAsync()).ClientMods.Single().Status.Should().Be(ServerSyncStatus.InSync);
        installed.Guid.Should().Be(ClientGuid);
    }

    // ---- applying ----

    [Fact]
    public async Task Apply_InstallsTheExactVersionTheServerRuns_FromTheForge()
    {
        ServerRuns("2.0.1");
        ForgeHas("2.0.1", forgeId: 77);

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Succeeded.Should().BeTrue();
        var request = _installer.Requests.Should().ContainSingle().Subject;
        request.ForgeId.Should().Be(77);
        request.ExpectedVersion.Should().Be(SemanticVersion.Parse("2.0.1"));
        request.DownloadUrl.Should().NotBeNull();
        request.InstanceId.Should().Be(_context.Instance.Id);
        (await PlanAsync()).ServerMods.Single().Status.Should().Be(ServerSyncStatus.InSync);
    }

    [Fact]
    public async Task Apply_ReplacesAServerModThatDiffers()
    {
        await InstallServerModAsync();
        ServerRuns("2.1.0");
        ForgeHas("2.1.0");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Outcomes.Should().ContainSingle().Which.Applied.Should().BeTrue();
        _installer.Requests.Single().ExpectedVersion.Should().Be(SemanticVersion.Parse("2.1.0"));
    }

    [Fact]
    public async Task Apply_LeavesAPinnedModAlone_AndSaysWhy()
    {
        await InstallServerModAsync();
        await _context.Installer.SetPinnedAsync(_context.Instance.Id, ServerGuid, pinned: true);
        ServerRuns("2.1.0");
        ForgeHas("2.1.0");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        _installer.Requests.Should().BeEmpty("a pin is a promise");
        result.Succeeded.Should().BeFalse();
        result.Outcomes.Should().ContainSingle().Which.Error.Should().Contain("Pinned");
    }

    [Fact]
    public async Task Apply_ChangesAPinnedMod_OnlyWhenAskedTo_AndKeepsItPinned()
    {
        await InstallServerModAsync();
        await _context.Installer.SetPinnedAsync(_context.Instance.Id, ServerGuid, pinned: true);
        ServerRuns("2.1.0");
        ForgeHas("2.1.0");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync(), new ServerSyncOptions { OverridePins = true });

        result.Succeeded.Should().BeTrue();
        _installer.Requests.Should().ContainSingle().Which.Pin.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_DoesNothing_ForAPlanThatIsInSync()
    {
        await InstallServerModAsync();
        ServerRuns("2.0.1");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Outcomes.Should().BeEmpty();
        result.Succeeded.Should().BeTrue();
        _installer.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_SaysSo_WhenTheModIsNotOnTheForge()
    {
        ServerRuns("2.0.1");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Succeeded.Should().BeFalse();
        result.Outcomes.Single().Error.Should().Contain("not on the Forge").And.Contain(ServerGuid).And.Contain("2.0.1");
        _installer.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_SaysSo_WhenTheForgeHasNoSuchVersion()
    {
        ServerRuns("9.9.9");
        ForgeHas("2.0.1");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Outcomes.Single().Error.Should().Contain("no version 9.9.9");
    }

    [Fact]
    public async Task Apply_SaysSo_WhenTheServerVersionIsNotAVersion()
    {
        ServerRuns("banana");
        ForgeHas("2.0.1");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Outcomes.Single().Error.Should().Contain("'banana'").And.Contain("not a version");
    }

    [Fact]
    public async Task Apply_GoesOnWithTheOtherMods_WhenOneFails()
    {
        ServerRuns("1.0.0", guid: "com.unknown.mod", name: "Unknown");
        ServerRuns("2.0.1");
        ForgeHas("2.0.1");

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Outcomes.Should().HaveCount(2);
        result.Outcomes.Should().ContainSingle(outcome => outcome.Entry.Guid == "com.unknown.mod" && !outcome.Applied);
        result.Outcomes.Should().ContainSingle(outcome => outcome.Entry.Guid == ServerGuid && outcome.Applied);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_NeverTouchesAModTheServerDoesNotRun()
    {
        await InstallServerModAsync();

        var result = await _service.ApplyAsync(_context.Instance.Id, await PlanAsync());

        result.Outcomes.Should().BeEmpty();
        _installer.Requests.Should().BeEmpty();
    }

    /// <summary>Passes every request to the real installer, and writes down what was asked.</summary>
    private sealed class RecordingInstaller : IModInstaller
    {
        public IModInstaller Inner { get; set; } = null!;

        public List<ModInstallRequest> Requests { get; } = [];

        public Task<ModInstallResult> InstallAsync(ModInstallRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Inner.InstallAsync(request, cancellationToken);
        }

        public Task UninstallAsync(Guid instanceId, string modGuid, bool force = false, CancellationToken cancellationToken = default)
        {
            return Inner.UninstallAsync(instanceId, modGuid, force, cancellationToken);
        }

        public Task SetPinnedAsync(Guid instanceId, string modGuid, bool pinned, CancellationToken cancellationToken = default)
        {
            return Inner.SetPinnedAsync(instanceId, modGuid, pinned, cancellationToken);
        }
    }
}
