using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class RunAdoptionServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private readonly InstanceRunStore _store;

    private readonly FakeInspector _inspector = new();

    private readonly FakeInspector _launchers = new();

    private readonly RunAdoptionService _service;

    private readonly DateTimeOffset _started = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    public RunAdoptionServiceTests()
    {
        var paths = _temp.CreateAppPaths();
        paths.EnsureCreated();
        _store = new InstanceRunStore(paths, NullLogger<InstanceRunStore>.Instance);
        _service = new RunAdoptionService(_store, _inspector, _launchers, NullLogger<RunAdoptionService>.Instance);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private RunProcess Process(int pid, string path = "/srv/SPT.Server.Linux", int? port = null)
    {
        return new RunProcess(pid, _started, path, RunMechanism.Setsid, port);
    }

    private InstanceRun Recorded(string name = "one", int serverPid = 700, int gamePid = 800, int launcherPid = 500)
    {
        var run = new InstanceRun
        {
            InstanceId = Guid.NewGuid(),
            InstanceName = name,
            LauncherProcessId = launcherPid,
        };

        run = run.WithServer(Process(serverPid, port: 6969));
        run = run.WithGame(new RunProcess(gamePid, _started.AddSeconds(5), "/srv/EscapeFromTarkov.exe", RunMechanism.SystemdScope));
        _store.Save(run);
        _inspector.Alive[serverPid] = new ProcessIdentity(serverPid, _started, "/srv/SPT.Server.Linux");
        _inspector.Alive[gamePid] = new ProcessIdentity(gamePid, _started.AddSeconds(5), "/srv/EscapeFromTarkov.exe");

        return run;
    }

    [Fact]
    public async Task List_AdoptsARunWhoseProcessesAreAlive()
    {
        Recorded();

        var runs = await _service.ListAsync();

        var run = runs.Should().ContainSingle().Subject;
        run.Server!.State.Should().Be(AdoptedProcessState.Running);
        run.Game!.State.Should().Be(AdoptedProcessState.Running);
        run.AnyRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Leftovers_AreOnlyRunsWhoseLauncherIsGone()
    {
        Recorded(launcherPid: 500);
        _launchers.Alive[500] = new ProcessIdentity(500, _started, "/usr/bin/dotnet");

        (await _service.FindLeftoversAsync()).Should().BeEmpty("another launcher owns these processes");
        _store.ReadAll().Should().HaveCount(1);
    }

    [Fact]
    public async Task Leftovers_AreFoundOnceTheLauncherIsGone()
    {
        var recorded = Recorded();

        var leftover = (await _service.FindLeftoversAsync()).Should().ContainSingle().Subject;

        leftover.IsLeftover.Should().BeTrue();
        leftover.Run.InstanceId.Should().Be(recorded.InstanceId);
        leftover.Server!.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Leftovers_ForgetARecordWhoseProcessesAreAllGone()
    {
        Recorded();
        _inspector.Alive.Clear();

        (await _service.FindLeftoversAsync()).Should().BeEmpty();
        _store.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Leftovers_ForgetARecordWhosePidsWereReused_AndNeverTouchThoseProcesses()
    {
        Recorded();
        _inspector.Alive[700] = new ProcessIdentity(700, _started.AddHours(2), "/srv/SPT.Server.Linux");
        _inspector.Alive[800] = new ProcessIdentity(800, _started.AddHours(2), "/srv/EscapeFromTarkov.exe");

        // Nothing of that run is ours any more, so there is nothing to show and the record has done its job.
        (await _service.FindLeftoversAsync())
            .Should()
            .BeEmpty();
        _store.ReadAll().Should().BeEmpty();
        _inspector.StopRequests.Should().BeEmpty("a recycled pid is somebody else's process");
    }

    [Fact]
    public async Task Leftovers_NeverTouchAProcessThatIsAnotherProgram()
    {
        Recorded();
        _inspector.Alive[700] = new ProcessIdentity(700, _started, "/usr/bin/firefox");

        var adopted = (await _service.FindLeftoversAsync()).Single();

        adopted.Server!.State.Should().Be(AdoptedProcessState.NotOurs);
    }

    [Fact]
    public async Task Stop_EndsTheGameAndTheServer_AndForgetsTheRecord()
    {
        Recorded();
        var leftover = (await _service.FindLeftoversAsync()).Single();

        var stopped = await _service.StopAsync(leftover);

        stopped.Should().Be(2);
        _inspector.StopRequests.Should().Equal([800, 700], "the game goes first: the server is what it needs");
        _store.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_KeepsTheRecord_WhenAProcessWillNotDie()
    {
        Recorded();
        var leftover = (await _service.FindLeftoversAsync()).Single();
        _inspector.CanStop = false;

        var stopped = await _service.StopAsync(leftover);

        stopped.Should().Be(0);
        _store.ReadAll().Should().HaveCount(1, "forgetting a process that still runs loses the only way to find it");
    }

    [Fact]
    public async Task Stop_DoesNothing_ForAProcessThatIsNotOurs()
    {
        Recorded();
        _inspector.Alive[700] = new ProcessIdentity(700, _started.AddHours(3), "/srv/SPT.Server.Linux");
        var adopted = (await _service.FindLeftoversAsync()).Single();

        (await _service.StopAsync(adopted)).Should().Be(1, "only the game is still recognised");
        _inspector.StopRequests.Should().Equal(800);
    }
}
