using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Launch;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Runtime;
using YuiTushonkaLauncher.Tests.Server;

namespace YuiTushonkaLauncher.Tests.Launch;

public sealed class LaunchServiceTests : IDisposable
{
    private readonly ServerWorld _world = new();

    private readonly TempDirectory _appData = new();

    private readonly InstanceRunStore _runs;

    private readonly FakeSupervisor _supervisor = new();

    private readonly FakeSptClient _client = new();

    private readonly FakeProfiles _profiles = new();

    private readonly FakePatcher _patcher = new();

    private readonly FakeBusyGuard _busy = new();

    private readonly FakeInspector _inspector = new();

    private readonly FakeSpawner _spawner = new();

    private readonly FakeRuntimeLauncher _runtime = new();

    private readonly FakeInstanceList _instances = new();

    private readonly FastTimeProvider _time = new();

    private readonly LaunchService _service;

    private Instance _instance = null!;

    public LaunchServiceTests()
    {
        var paths = _appData.CreateAppPaths();
        paths.EnsureCreated();
        _runs = new InstanceRunStore(paths, NullLogger<InstanceRunStore>.Instance);

        _service = new LaunchService(
            _instances,
            _supervisor,
            new SptServerLocator(new FakePlatform(linux: true)),
            new FakeClientFactory(_client),
            _profiles,
            _patcher,
            _runtime,
            new FakeRuntimeProfiles(),
            _spawner,
            _runs,
            _busy,
            _inspector,
            _time,
            NullLogger<LaunchService>.Instance
        );

        _instance = _world.CreateInstance("one");
        _instance.Patch.IsPatched = true;
        _instances.Instances.Add(_instance);
        File.WriteAllText(Path.Combine(_instance.Path, "EscapeFromTarkov.exe"), "MZ");
        File.WriteAllText(Path.Combine(_instance.Path, "UnityPlayer.dll"), "unity");
        File.WriteAllText(Path.Combine(_instance.Path, "winhttp.dll"), "doorstop");
        File.WriteAllText(Path.Combine(_instance.Path, "doorstop_config.ini"), "[General]");
        Directory.CreateDirectory(Path.Combine(_instance.Path, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(_instance.Path, "BepInEx", "core", "BepInEx.Preloader.dll"), "preloader");
        _client.Profiles.Add(FakeSptClient.NewProfile("yui", "Standard", "profile-1") with { PendingWipe = false });
    }

    public void Dispose()
    {
        _world.Dispose();
        _appData.Dispose();
    }

    private LaunchRequest Request(string? profile = "yui")
    {
        return new LaunchRequest { InstanceId = _instance.Id, ProfileUsername = profile };
    }

    // ---- pre-flight ----

    [Fact]
    public async Task Plan_IsReady_WhenEverythingIsThere()
    {
        var plan = await _service.PlanAsync(Request());

        plan.CanLaunch.Should().BeTrue();
        plan.ProfileUsername.Should().Be("yui");
        plan.PatchAlreadyApplied.Should().BeTrue();
        plan.ServerAlreadyRunning.Should().BeFalse();
        plan.GameCommand!.FileName.Should().Be(Path.Combine(_instance.Path, "EscapeFromTarkov.exe"));
    }

    [Fact]
    public async Task Plan_RefusesAnInstanceThatDoesNotExist()
    {
        var plan = await _service.PlanAsync(new LaunchRequest { InstanceId = Guid.NewGuid(), ProfileUsername = "yui" });

        plan.CanLaunch.Should().BeFalse();
        plan.Problems.Should().ContainSingle().Which.Code.Should().Be(LaunchProblemCode.InstanceMissing);
    }

    [Fact]
    public async Task Plan_RefusesASplitInstance_AndSaysToMigrateIt()
    {
        // The installer's shape: the executable is under gamefiles/, which SPT cannot run.
        var split = _world.CreateInstance("split");
        split.Path = MoveToSplit(split);
        _instances.Instances.Add(split);

        var plan = await _service.PlanAsync(new LaunchRequest { InstanceId = split.Id, ProfileUsername = "yui" });

        plan.CanLaunch.Should().BeFalse();
        plan.Problems.Should()
            .Contain(problem => problem.Code == LaunchProblemCode.NotMerged && problem.Message.Contains("--migrate-layout"));
    }

    [Fact]
    public async Task Plan_ListsEveryMissingFile()
    {
        File.Delete(Path.Combine(_instance.Path, "winhttp.dll"));
        Directory.Delete(Path.Combine(_instance.Path, "BepInEx"), recursive: true);

        var plan = await _service.PlanAsync(Request());

        var problem = plan.Problems.Should().ContainSingle(entry => entry.Code == LaunchProblemCode.FilesMissing).Subject;
        problem.Message.Should().Contain("winhttp.dll").And.Contain("BepInEx/core/BepInEx.Preloader.dll");
    }

    [Fact]
    public async Task Plan_RefusesWithoutAProfile_WhenNothingIsRemembered()
    {
        var plan = await _service.PlanAsync(Request(profile: null));

        plan.CanLaunch.Should().BeFalse();
        plan.Problems.Should().ContainSingle().Which.Code.Should().Be(LaunchProblemCode.ProfileMissing);
    }

    [Fact]
    public async Task Plan_UsesTheRememberedProfile_WhenNoneWasGiven()
    {
        _profiles.Remembered = "remembered-user";

        var plan = await _service.PlanAsync(Request(profile: null));

        plan.ProfileUsername.Should().Be("remembered-user");
    }

    [Fact]
    public async Task Plan_RefusesWhenTheRuntimeCannotRunTheGame()
    {
        _runtime.Problems.Add(
            new RuntimeProblem(RuntimeProblemSeverity.Error, RuntimeProblemCode.NoProfile, "No runtime profile is selected.")
        );

        var plan = await _service.PlanAsync(Request());

        plan.CanLaunch.Should().BeFalse();
        plan.Problems.Should().Contain(problem => problem.Code == LaunchProblemCode.RuntimeUnavailable);
    }

    [Fact]
    public async Task Plan_ChangesNothing()
    {
        await _service.PlanAsync(Request());

        _supervisor.Starts.Should().Be(0);
        _spawner.Starts.Should().BeEmpty();
        _runs.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_SaysTheServerWouldBeAdopted_WhenTheRecordedOneIsAlive()
    {
        _runs.Save(
            new InstanceRun
            {
                InstanceId = _instance.Id,
                InstanceName = "one",
                LauncherProcessId = 1,
            }.WithServer(new RunProcess(777, _time.GetUtcNow(), "/srv/SPT.Server.Linux", RunMechanism.Setsid, 6969))
        );
        _inspector.Alive[777] = new ProcessIdentity(777, _time.GetUtcNow(), "/srv/SPT.Server.Linux");

        var plan = await _service.PlanAsync(Request());

        plan.ServerAlreadyRunning.Should().BeTrue();
    }

    // ---- the pipeline ----

    [Fact]
    public async Task Launch_RunsEveryStepInOrder_AndDetachesTheGame()
    {
        var steps = new List<LaunchProgress>();

        var result = await _service.LaunchAsync(Request(), new CollectingProgress(steps));

        result.Succeeded.Should().BeTrue();
        result.ServerProcessId.Should().Be(4321);
        result.GameProcessId.Should().Be(_spawner.Last.ProcessId);
        result.ProfileId.Should().Be("profile-1");

        steps
            .Where(step => step.Outcome == LaunchStepOutcome.Succeeded)
            .Select(step => step.Step)
            .Distinct()
            .Should()
            .Equal(
                LaunchStep.PreFlight,
                LaunchStep.PatchCheck,
                LaunchStep.ServerStart,
                LaunchStep.Ready,
                LaunchStep.GameStart,
                LaunchStep.Detached
            );

        _supervisor.Starts.Should().Be(1);
        _supervisor.Options.Single()!.RestartOnCrash.Should().BeFalse("a launch starts a server, it does not install a watchdog");
        _spawner.Starts.Should().ContainSingle();
        _profiles.Selected.Should().Equal("yui");
    }

    [Fact]
    public async Task Launch_TellsTheGameWhichServerAndProfileToUse()
    {
        await _service.LaunchAsync(Request(), new CollectingProgress([]));

        var arguments = _spawner.Starts.Single().Command.Arguments;
        arguments.Should().Contain("-token=profile-1");
        arguments.Should().Contain(argument => argument.Contains("'BackendUrl':'https://127.0.0.1:6969'", StringComparison.Ordinal));
        _spawner.Starts.Single().Command.WorkingDirectory.Should().Be(_instance.Path);
    }

    [Fact]
    public async Task Launch_RecordsTheGameNextToTheServer()
    {
        await _service.LaunchAsync(Request(), new CollectingProgress([]));

        var run = _runs.ReadAll().Should().ContainSingle().Subject;
        run.Game!.ProcessId.Should().Be(_spawner.Last.ProcessId);
        run.Game.Mechanism.Should().Be(_spawner.Mechanism);
        run.Game.ExecutablePath.Should().Be(Path.Combine(_instance.Path, "EscapeFromTarkov.exe"));
    }

    [Fact]
    public async Task Launch_PatchesOnlyWhenTheInstanceNeedsIt()
    {
        _instance.Patch.IsPatched = false;

        await _service.LaunchAsync(Request(), new CollectingProgress([]));

        _patcher.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Launch_DoesNotPatch_WhenAlreadyPatchedOrAskedToSkip()
    {
        await _service.LaunchAsync(Request(), new CollectingProgress([]));
        _patcher.Calls.Should().Be(0);

        _instance.Patch.IsPatched = false;
        await _service.LaunchAsync(Request() with { SkipPatch = true }, new CollectingProgress([]));
        _patcher.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Launch_RefusesToPatch_WhileThePairRuns()
    {
        _instance.Patch.IsPatched = false;
        _busy.Activity.Add(new InstanceActivity("the game", 900, "/srv/EscapeFromTarkov.exe"));

        var act = () => _service.LaunchAsync(Request(), new CollectingProgress([]));

        await act.Should().ThrowAsync<InstanceBusyException>();
        _patcher.Calls.Should().Be(0);
        _spawner.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Launch_ReportsAFailedPatch_WithTheReason()
    {
        _instance.Patch.IsPatched = false;
        _patcher.Fail = true;

        var act = () => _service.LaunchAsync(Request(), new CollectingProgress([]));

        var failure = (await act.Should().ThrowAsync<LaunchException>()).Which;
        failure
            .Problems.Should()
            .ContainSingle(problem => problem.Code == LaunchProblemCode.PatchFailed && problem.Message.Contains("corrupt"));
        _supervisor.Starts.Should().Be(0, "nothing starts after a failed patch");
    }

    [Fact]
    public async Task Launch_ReportsAFailedServerStart()
    {
        _supervisor.ShouldThrow = true;

        var act = () => _service.LaunchAsync(Request(), new CollectingProgress([]));

        var failure = (await act.Should().ThrowAsync<LaunchException>()).Which;
        failure.Message.Should().Contain("bad mod");
        _spawner.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Launch_DoesNotStartASecondServer_WhenOneIsAlreadyRunning()
    {
        _supervisor.SetStatus(_instance.Id, ServerState.Running, pid: 1234);

        var result = await _service.LaunchAsync(Request(), new CollectingProgress([]));

        _supervisor.Starts.Should().Be(0);
        result.ServerProcessId.Should().Be(1234);
    }

    [Fact]
    public async Task Launch_RefusesAProfileTheServerDoesNotHave_AndNamesThoseItHas()
    {
        var act = () => _service.LaunchAsync(Request(profile: "ghost"), new CollectingProgress([]));

        var failure = (await act.Should().ThrowAsync<LaunchException>()).Which;
        failure.Message.Should().Contain("'ghost'").And.Contain("yui");
        _spawner.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Launch_RefusesAProfileTheServerCouldNotLoad()
    {
        _client.Profiles.Clear();
        _client.Profiles.Add(FakeSptClient.NewProfile("broken", "Standard") with { InvalidOrUnloadable = true });

        var act = () => _service.LaunchAsync(Request(profile: "broken"), new CollectingProgress([]));

        (await act.Should().ThrowAsync<LaunchException>())
            .Which.Problems.Should()
            .Contain(problem => problem.Code == LaunchProblemCode.ProfileUnloadable);
    }

    [Fact]
    public async Task Launch_RemembersTheProfile_OnlyWhenAskedTo()
    {
        await _service.LaunchAsync(Request(), new CollectingProgress([]));
        _profiles.RememberedWrites.Should().BeEmpty();

        await _service.LaunchAsync(Request() with { RememberProfile = true }, new CollectingProgress([]));
        _profiles.RememberedWrites.Should().Equal(("yui", "profile-1"));
    }

    [Fact]
    public async Task Launch_CanBeCancelled_BeforeTheGameStarts()
    {
        using var cancellation = new CancellationTokenSource();
        var steps = new List<LaunchProgress>();
        _supervisor.OnStart = _ =>
        {
            cancellation.Cancel();
            return new ServerStatus
            {
                InstanceId = _instance.Id,
                State = ServerState.Running,
                ProcessId = 4321,
            };
        };

        var act = () => _service.LaunchAsync(Request(), new CollectingProgress(steps), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _spawner.Starts.Should().BeEmpty("a cancelled launch leaves no game behind");
        steps.Should().Contain(step => step.Outcome == LaunchStepOutcome.Cancelled);
    }

    [Fact]
    public async Task Launch_RefusesWhenTheGameCannotBeStarted()
    {
        _spawner.Factory = _ => throw new InvalidOperationException("Permission denied");

        var act = () => _service.LaunchAsync(Request(), new CollectingProgress([]));

        (await act.Should().ThrowAsync<LaunchException>())
            .Which.Problems.Should()
            .Contain(problem => problem.Code == LaunchProblemCode.GameFailed);
        _runs.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Launch_ReportsTheWholePipelineAsFailed_WhenThePreFlightFails()
    {
        File.Delete(Path.Combine(_instance.Path, "winhttp.dll"));
        var steps = new List<LaunchProgress>();

        var act = () => _service.LaunchAsync(Request(), new CollectingProgress(steps));

        await act.Should().ThrowAsync<LaunchException>();
        steps
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<LaunchProgress>(step => step.Step == LaunchStep.PreFlight && step.Outcome == LaunchStepOutcome.Failed);
    }

    /// <summary>Moves a merged instance back into the installer's split shape.</summary>
    private static string MoveToSplit(Instance instance)
    {
        var runtime = Path.Combine(instance.Path, "SPT_Runtime");
        var split = instance.Path + "-split";
        Directory.CreateDirectory(Path.Combine(split, "gamefiles"));
        Directory.CreateDirectory(Path.Combine(split, "spt"));

        foreach (var entry in Directory.EnumerateFileSystemEntries(instance.Path))
        {
            var name = Path.GetFileName(entry);
            var isGame = name is "EscapeFromTarkov.exe" or "UnityPlayer.dll" or "EscapeFromTarkov_Data";
            var target = Path.Combine(split, isGame ? "gamefiles" : "spt", name);
            Directory.Move(entry, target);
        }

        Directory.Delete(instance.Path, recursive: true);
        Directory.Move(split, instance.Path);
        runtime.Should().NotBeNull();
        return instance.Path;
    }

    private sealed class CollectingProgress(List<LaunchProgress> steps) : IProgress<LaunchProgress>
    {
        public void Report(LaunchProgress value)
        {
            lock (steps)
            {
                steps.Add(value);
            }
        }
    }
}
