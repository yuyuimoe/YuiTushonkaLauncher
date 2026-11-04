using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Launch;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Server;

namespace YuiTushonkaLauncher.Tests.Launch;

public sealed class InstanceBusyGuardTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private readonly InstanceRunStore _runs;

    private readonly FakeSupervisor _supervisor = new();

    private readonly FakeInspector _inspector = new();

    private readonly FakeScopes _scopes = new();

    private readonly InstanceBusyGuard _guard;

    private readonly Guid _instanceId = Guid.NewGuid();

    public InstanceBusyGuardTests()
    {
        var paths = _temp.CreateAppPaths();
        paths.EnsureCreated();
        _runs = new InstanceRunStore(paths, NullLogger<InstanceRunStore>.Instance);
        _guard = new InstanceBusyGuard(_supervisor, _runs, _inspector, _scopes);
        _supervisor.SetStatus(_instanceId, ServerState.Stopped);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private void Record(RunProcess? server = null, RunProcess? game = null)
    {
        var run = new InstanceRun
        {
            InstanceId = _instanceId,
            InstanceName = "one",
            LauncherProcessId = 1,
        };
        _runs.Save(run.WithServer(server).WithGame(game));
    }

    private static RunProcess Process(int pid, string path = "/srv/x", RunMechanism mechanism = RunMechanism.Setsid, string? scope = null)
    {
        return new RunProcess(pid, DateTimeOffset.UtcNow, path, mechanism, null, scope);
    }

    [Fact]
    public async Task AnIdleInstance_IsIdle()
    {
        (await _guard.FindActivityAsync(_instanceId)).Should().BeEmpty();
        await _guard.EnsureIdleAsync(_instanceId, "Patching");
    }

    [Fact]
    public async Task AServerSupervisedInThisSession_Counts()
    {
        _supervisor.SetStatus(_instanceId, ServerState.Running, pid: 555);

        var activity = (await _guard.FindActivityAsync(_instanceId)).Should().ContainSingle().Subject;

        activity.Kind.Should().Be("the server");
        activity.ProcessId.Should().Be(555);
    }

    [Theory]
    [InlineData(ServerState.Starting)]
    [InlineData(ServerState.Running)]
    [InlineData(ServerState.Stopping)]
    public async Task AServerThatIsStartingOrStopping_AlsoCounts(ServerState state)
    {
        _supervisor.SetStatus(_instanceId, state, pid: 555);

        (await _guard.FindActivityAsync(_instanceId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task ARecordedServerThatIsAliveByPid_Counts()
    {
        Record(server: Process(700, "/srv/SPT.Server.Linux"));
        _inspector.Alive[700] = new ProcessIdentity(700, DateTimeOffset.UtcNow, "/srv/SPT.Server.Linux");

        (await _guard.FindActivityAsync(_instanceId)).Should().ContainSingle().Which.Kind.Should().Be("the server");
    }

    [Fact]
    public async Task ARecordedGameThatIsAliveByProcessGroup_Counts()
    {
        Record(game: Process(800, "/srv/EscapeFromTarkov.exe"));
        _inspector.Groups.Add(800);

        (await _guard.FindActivityAsync(_instanceId)).Should().ContainSingle().Which.Kind.Should().Be("the game");
    }

    [Fact]
    public async Task ARecordedGameThatIsAliveByScope_Counts()
    {
        Record(game: Process(900, "/srv/EscapeFromTarkov.exe", RunMechanism.SystemdScope, scope: "ytt-abc"));
        _scopes.Active.Add("ytt-abc");

        (await _guard.FindActivityAsync(_instanceId)).Should().ContainSingle().Which.Kind.Should().Be("the game");
    }

    [Fact]
    public async Task ARecordWhoseProcessesAreGone_DoesNotCount()
    {
        Record(server: Process(700), game: Process(800));

        (await _guard.FindActivityAsync(_instanceId)).Should().BeEmpty();
    }

    [Fact]
    public async Task BothCount_AndTheGuardsMessageNamesThem()
    {
        _supervisor.SetStatus(_instanceId, ServerState.Running, pid: 555);
        Record(game: Process(800, "/srv/EscapeFromTarkov.exe"));
        _inspector.Groups.Add(800);

        var activity = await _guard.FindActivityAsync(_instanceId);

        activity.Select(entry => entry.Kind).Should().BeEquivalentTo("the server", "the game");

        var act = () => _guard.EnsureIdleAsync(_instanceId, "Patching");

        var failure = (await act.Should().ThrowAsync<InstanceBusyException>()).Which;
        failure.Message.Should().Contain("Patching").And.Contain("the server").And.Contain("the game").And.Contain("Stop it first");
        failure.Activity.Should().HaveCount(2);
    }

    [Fact]
    public async Task AProcessThatIsBothSupervisedAndRecorded_IsListedOnce()
    {
        _supervisor.SetStatus(_instanceId, ServerState.Running, pid: 700);
        Record(server: Process(700));
        _inspector.Alive[700] = new ProcessIdentity(700, DateTimeOffset.UtcNow, "/srv/x");

        (await _guard.FindActivityAsync(_instanceId)).Should().ContainSingle();
    }
}
