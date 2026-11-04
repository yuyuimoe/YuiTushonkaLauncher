using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class SptServerSupervisorTests : IDisposable
{
    private readonly SupervisorHarness _h = new();

    public void Dispose()
    {
        _h.Dispose();
    }

    // ---- starting ----

    [Fact]
    public async Task Start_BringsTheServerUp_AndReportsWhatItLaunched()
    {
        var instance = _h.AddInstance("one", port: 6970);
        var states = new List<ServerState>();
        _h.Supervisor.StatusChanged += (_, status) => states.Add(status.State);

        var status = await _h.Supervisor.StartAsync(instance);

        status.State.Should().Be(ServerState.Running);
        status.ProcessId.Should().Be(_h.Spawner.Last.ProcessId);
        status.Endpoint!.Port.Should().Be(6970);
        status.ReadyAfter.Should().NotBeNull();
        states.Should().Equal(ServerState.Starting, ServerState.Starting, ServerState.Running);

        var start = _h.Spawner.Starts.Should().ContainSingle().Subject;
        start.Command.FileName.Should().EndWith("SPT.Server.Linux");
        start.Command.WorkingDirectory.Should().EndWith("SPT_Runtime");
        _h.RuntimeLauncher.Requests.Should()
            .ContainSingle()
            .Which.Target.Should()
            .Be(ProgramTarget.NativeProgram, "the Linux server is not a Windows program");
    }

    [Fact]
    public async Task Start_LeavesARecordOfTheServer_SoACrashedLauncherCanBeCleanedUpAfter()
    {
        var instance = _h.AddInstance("one");

        await _h.Supervisor.StartAsync(instance);

        var run = _h.Runs.ReadAll().Should().ContainSingle().Subject;
        var server = run.Server!;
        server.Should().NotBeNull();

        run.InstanceId.Should().Be(instance.Id);
        run.LauncherProcessId.Should().Be(Environment.ProcessId);
        server.ProcessId.Should().Be(_h.Spawner.Last.ProcessId);
        server.ExecutablePath.Should().EndWith("SPT.Server.Linux");
        server.Port.Should().Be(6969);
        server.Mechanism.Should().Be(RunMechanism.Setsid, "the machine reports how it detached, so the UI can explain it");
    }

    [Fact]
    public async Task Start_RefusesASecondServerForTheSameInstance()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);

        var act = () => _h.Supervisor.StartAsync(instance);

        var failure = (await act.Should().ThrowAsync<ServerStartException>()).Which;
        failure.Problems.Should().Contain(problem => problem.Code == ServerProblemCode.AlreadyRunning);
        _h.Spawner.Starts.Should().HaveCount(1);
    }

    [Fact]
    public async Task Start_RefusesAPortThatAnotherRunningInstanceUses_AndNamesIt()
    {
        var first = _h.AddInstance("first", port: 6969);
        var second = _h.AddInstance("second", port: 6969);
        await _h.Supervisor.StartAsync(first);

        var act = () => _h.Supervisor.StartAsync(second);

        var failure = (await act.Should().ThrowAsync<ServerStartException>()).Which;
        failure
            .Problems.Should()
            .Contain(problem => problem.Code == ServerProblemCode.PortUsedByRunningInstance && problem.Message.Contains("'first'"));
        _h.Spawner.Starts.Should().HaveCount(1, "nothing was started for the second instance");
    }

    [Fact]
    public async Task Start_AllowsTwoInstancesOnDifferentPorts()
    {
        var first = _h.AddInstance("first", port: 6969);
        var second = _h.AddInstance("second", port: 6970);

        await _h.Supervisor.StartAsync(first);
        await _h.Supervisor.StartAsync(second);

        _h.Supervisor.GetStatus(first.Id).State.Should().Be(ServerState.Running);
        _h.Supervisor.GetStatus(second.Id).State.Should().Be(ServerState.Running);
    }

    [Fact]
    public async Task Start_RefusesABusyPort_AndSuggestsTheNextOneNobodyUses()
    {
        var instance = _h.AddInstance("one", port: 6969);
        _h.AddInstance("other", port: 6970);
        _h.Ports.Busy.Add(6969);

        var act = () => _h.Supervisor.StartAsync(instance);

        var failure = (await act.Should().ThrowAsync<ServerStartException>()).Which;
        failure.Problems.Should().Contain(problem => problem.Code == ServerProblemCode.PortBusy);
        failure.Message.Should().Contain("6969").And.Contain("next free: 6971", "6970 is configured for another instance");
        _h.Spawner.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_OnlyWarns_WhenAnotherInstanceIsConfiguredForTheSamePort()
    {
        var instance = _h.AddInstance("one", port: 6969);
        _h.AddInstance("other", port: 6969);

        var plan = await _h.Supervisor.PlanAsync(instance);

        plan.CanStart.Should().BeTrue();
        plan.Problems.Should()
            .ContainSingle(problem => problem.Code == ServerProblemCode.PortConfiguredByOtherInstance && !problem.IsError);
    }

    [Fact]
    public async Task Plan_ChangesNothing()
    {
        var instance = _h.AddInstance("one");

        await _h.Supervisor.PlanAsync(instance);

        _h.Spawner.Starts.Should().BeEmpty();
        _h.Runs.ReadAll().Should().BeEmpty();
        _h.Supervisor.GetStatus(instance.Id).State.Should().Be(ServerState.Stopped);
    }

    [Fact]
    public async Task Start_RefusesWhenTheDotNetRuntimeIsTooOld_AndSaysWhatToInstall()
    {
        var instance = _h.AddInstance("one");
        _h.DotNet.Installed = [new("Microsoft.NETCore.App", "9.0.21"), new("Microsoft.AspNetCore.App", "9.0.21")];

        var act = () => _h.Supervisor.StartAsync(instance);

        var failure = (await act.Should().ThrowAsync<ServerStartException>()).Which;
        failure.Problems.Count(problem => problem.Code == ServerProblemCode.RuntimeMissing).Should().Be(2);
        failure
            .Message.Should()
            .Contain("Microsoft.AspNetCore.App 10.0.0")
            .And.Contain("only 9.0.21")
            .And.Contain("Install the .NET 10 runtime");
        _h.Spawner.Starts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("10.0.10", true)]
    [InlineData("10.2.0", true)]
    [InlineData("11.0.0", false)]
    [InlineData("9.0.99", false)]
    public async Task Start_AcceptsANewerMinorOfTheSameMajor_ButNeverAnotherMajor(string installed, bool accepted)
    {
        var instance = _h.AddInstance("one");
        _h.DotNet.Installed = [new("Microsoft.NETCore.App", installed), new("Microsoft.AspNetCore.App", installed)];

        var plan = await _h.Supervisor.PlanAsync(instance);

        plan.CanStart.Should().Be(accepted);
    }

    [Fact]
    public async Task Start_ProceedsWithAWarning_WhenTheRuntimeCannotBeListed()
    {
        var instance = _h.AddInstance("one");
        _h.DotNet.Installed = null;

        var plan = await _h.Supervisor.PlanAsync(instance);

        plan.CanStart.Should().BeTrue("the server finds its own runtime; only our check is blind");
        plan.Problems.Should().Contain(problem => problem.Code == ServerProblemCode.RuntimeUnverified && !problem.IsError);
    }

    [Fact]
    public async Task Start_RefusesAnInstanceWithoutAServerExecutable()
    {
        var instance = _h.AddInstance("one");
        File.Delete(Path.Combine(ServerWorld.RuntimeOf(instance), "SPT.Server.Linux"));

        var act = () => _h.Supervisor.StartAsync(instance);

        (await act.Should().ThrowAsync<ServerStartException>()).Which.Message.Should().Contain("SPT.Server.Linux");
        _h.Spawner.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_RefusesWhenTheRuntimeProfileIsInvalid()
    {
        var instance = _h.AddInstance("one");
        _h.RuntimeLauncher.Problems.Add(
            new RuntimeProblem(RuntimeProblemSeverity.Error, RuntimeProblemCode.EnvironmentInvalid, "WINEPREFIX is reserved.")
        );

        var act = () => _h.Supervisor.StartAsync(instance);

        (await act.Should().ThrowAsync<ServerStartException>())
            .Which.Problems.Should()
            .Contain(problem => problem.Code == ServerProblemCode.RuntimeProfileInvalid);
        _h.Spawner.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_WarnsWhenTheServerCannotBeTiedToTheLauncher()
    {
        var instance = _h.AddInstance("one");
        _h.Spawner.Mechanism = RunMechanism.None;

        var plan = await _h.Supervisor.PlanAsync(instance);

        plan.CanStart.Should().BeTrue();
        plan.Problems.Should().ContainSingle(problem => problem.Code == ServerProblemCode.DetachUnavailable);
    }

    // ---- start failures ----

    [Fact]
    public async Task Start_ExplainsABusyPort_EvenThoughTheServerExitsWithCodeZero()
    {
        var instance = _h.AddInstance("one", port: 6969);
        _h.Client.Up = false;
        _h.Spawner.Factory = start =>
        {
            SupervisorHarness.Say(
                start,
                "Failed to start the web server on 0.0.0.0:6969. Socket error:",
                "AddressAlreadyInUse (98).",
                "Press any key to exit..."
            );
            var child = new FakeChild(4101, _h.Time.GetUtcNow());
            child.Exit(0);
            return child;
        };

        var act = () => _h.Supervisor.StartAsync(instance);

        (await act.Should().ThrowAsync<ServerStartException>())
            .Which.Message.Should()
            .Contain("could not listen on port 6969")
            .And.Contain("another program");

        var status = _h.Supervisor.GetStatus(instance.Id);
        status.State.Should().Be(ServerState.Failed);
        status.ExitCode.Should().Be(0, "a failure with exit code zero is the whole point of this test");
        status.ProcessId.Should().BeNull();
        _h.Runs.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Start_ReportsTheExitCodeAndTheLastOutput_WhenTheServerDiesBeforePing()
    {
        var instance = _h.AddInstance("one");
        _h.Client.Up = false;
        _h.Spawner.Factory = start =>
        {
            SupervisorHarness.Say(start, "Loading mods...", "Unhandled exception: bad mod");
            var child = new FakeChild(4102, _h.Time.GetUtcNow());
            child.Exit(134);
            return child;
        };

        var act = () => _h.Supervisor.StartAsync(instance);

        (await act.Should().ThrowAsync<ServerStartException>())
            .Which.Message.Should()
            .Contain("exited with code 134")
            .And.Contain("Unhandled exception: bad mod");
    }

    [Fact]
    public async Task Start_KillsTheServer_WhenItNeverAnswers()
    {
        var instance = _h.AddInstance("one");
        _h.Client.Up = false;

        var act = () => _h.Supervisor.StartAsync(instance, new ServerStartOptions { ReadyTimeout = TimeSpan.FromSeconds(10) });

        (await act.Should().ThrowAsync<ServerStartException>()).Which.Message.Should().Contain("did not answer ping within 10 seconds");
        _h.Spawner.Last.WasKilled.Should().BeTrue("a server that never became ready must not be left running");
        _h.Runs.ReadAll().Should().BeEmpty();
        _h.Supervisor.GetStatus(instance.Id).State.Should().Be(ServerState.Failed);
    }

    [Fact]
    public async Task Start_DoesNotTrustAPingFromAnotherServer()
    {
        var instance = _h.AddInstance("one");
        _h.Client.PingBehavior = _ =>
        {
            _h.Spawner.Last.Exit(0);
            return Task.FromResult(true);
        };

        var act = () => _h.Supervisor.StartAsync(instance);

        await act.Should().ThrowAsync<ServerStartException>();
        _h.Supervisor.GetStatus(instance.Id).State.Should().Be(ServerState.Failed);
    }

    [Fact]
    public async Task Start_CanBeRetried_AfterAFailure()
    {
        var instance = _h.AddInstance("one");
        _h.Client.Up = false;
        await FluentActions
            .Awaiting(() => _h.Supervisor.StartAsync(instance, new ServerStartOptions { ReadyTimeout = TimeSpan.FromSeconds(2) }))
            .Should()
            .ThrowAsync<ServerStartException>();

        _h.Client.Up = true;
        var status = await _h.Supervisor.StartAsync(instance);

        status.State.Should().Be(ServerState.Running);
    }

    [Fact]
    public async Task Start_ReportsAProcessThatCannotBeStarted()
    {
        var instance = _h.AddInstance("one");
        _h.Spawner.Factory = _ => throw new InvalidOperationException("Permission denied");

        var act = () => _h.Supervisor.StartAsync(instance);

        (await act.Should().ThrowAsync<ServerStartException>()).Which.Message.Should().Contain("Permission denied");
        _h.Supervisor.GetStatus(instance.Id).State.Should().Be(ServerState.Failed);
    }

    // ---- stopping ----

    [Fact]
    public async Task Stop_AsksPolitely_AndDoesNotKillAServerThatLeaves()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);

        var status = await _h.Supervisor.StopAsync(instance.Id);

        _h.Spawner.Last.StopWasRequested.Should().BeTrue();
        _h.Spawner.Last.WasKilled.Should().BeFalse();
        status.State.Should().Be(ServerState.Stopped);
        status.ExitCode.Should().Be(0);
        status.ProcessId.Should().BeNull();
        _h.Runs.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_KillsAServerThatIgnoresTheRequest_AfterTheGracePeriod()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);
        _h.Spawner.Last.OnRequestStop = _ => { };

        var status = await _h.Supervisor.StopAsync(instance.Id, TimeSpan.FromSeconds(3));

        _h.Spawner.Last.StopWasRequested.Should().BeTrue();
        _h.Spawner.Last.WasKilled.Should().BeTrue();
        status.State.Should().Be(ServerState.Stopped);
        status.ExitCode.Should().Be(137);
        _h.Runs.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_IsHarmless_WhenNothingRuns_OrWhenCalledTwice()
    {
        var instance = _h.AddInstance("one");

        (await _h.Supervisor.StopAsync(instance.Id)).State.Should().Be(ServerState.Stopped);
        (await _h.Supervisor.StopAsync(Guid.NewGuid())).State.Should().Be(ServerState.Stopped);

        await _h.Supervisor.StartAsync(instance);
        await _h.Supervisor.StopAsync(instance.Id);
        (await _h.Supervisor.StopAsync(instance.Id)).State.Should().Be(ServerState.Stopped);
    }

    [Fact]
    public async Task Start_WorksAgain_AfterAStop()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);
        await _h.Supervisor.StopAsync(instance.Id);

        var status = await _h.Supervisor.StartAsync(instance);

        status.State.Should().Be(ServerState.Running);
        _h.Spawner.Children.Should().HaveCount(2);
    }

    [Fact]
    public async Task Stop_WhileStarting_CancelsTheStart_AndLeavesNothingRunning()
    {
        var instance = _h.AddInstance("one");
        var asked = new TaskCompletionSource();
        _h.Client.PingBehavior = async token =>
        {
            asked.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return false;
        };

        var starting = _h.Supervisor.StartAsync(instance);
        await asked.Task;
        await _h.Supervisor.StopAsync(instance.Id);

        await FluentActions.Awaiting(() => starting).Should().ThrowAsync<OperationCanceledException>();
        _h.Spawner.Last.WasKilled.Should().BeTrue();
        _h.Supervisor.GetStatus(instance.Id).State.Should().Be(ServerState.Stopped);
        _h.Runs.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Start_HonoursTheCallersCancellation()
    {
        var instance = _h.AddInstance("one");
        using var cancellation = new CancellationTokenSource();
        _h.Client.PingBehavior = _ =>
        {
            cancellation.Cancel();
            return Task.FromResult(false);
        };

        await FluentActions
            .Awaiting(() => _h.Supervisor.StartAsync(instance, null, cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        _h.Spawner.Last.WasKilled.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_LeavesTheDetachedServersRunning()
    {
        await _h.Supervisor.StartAsync(_h.AddInstance("one", port: 6969));
        await _h.Supervisor.StartAsync(_h.AddInstance("two", port: 6970));

        await _h.Supervisor.DisposeAsync();

        // D22: the launcher exiting is not a stop. The record stays so the next session can adopt the pair.
        _h.Spawner.Children.Should().OnlyContain(child => !child.StopWasRequested && !child.WasKilled);
        _h.Runs.ReadAll().Should().HaveCount(2);
    }

    [Fact]
    public async Task Plan_WhileTheServerRuns_SaysSo_AndWillNotStartASecondOne()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);

        var plan = await _h.Supervisor.PlanAsync(instance);

        plan.CanStart.Should().BeFalse();
        plan.Problems.Should()
            .ContainSingle(problem => problem.Code == ServerProblemCode.AlreadyRunning && problem.Message.Contains("process"));
        plan.Problems.Should().NotContain(problem => problem.Code == ServerProblemCode.PortBusy, "its own port is of course in use");
    }

    [Fact]
    public async Task Plan_SkipsTheRuntimeCheck_WhenTheServerDeclaresNoRequirement()
    {
        var instance = _h.AddInstance("one");
        File.Delete(Path.Combine(ServerWorld.RuntimeOf(instance), "SPT.Server.Linux.runtimeconfig.json"));
        _h.DotNet.Installed = [];

        var plan = await _h.Supervisor.PlanAsync(instance);

        plan.CanStart.Should().BeTrue();
        plan.Problems.Select(problem => problem.Code)
            .Should()
            .NotContain([ServerProblemCode.RuntimeMissing, ServerProblemCode.RuntimeUnverified]);
    }

    [Fact]
    public async Task Start_StillWorks_WhenTheRecordOfTheServerCannotBeWritten()
    {
        var instance = _h.AddInstance("one");
        File.WriteAllText(Path.Combine(_h.World.Temp.Combine("data"), "run"), "a file where the run folder should be");

        var status = await _h.Supervisor.StartAsync(instance);

        status.State.Should().Be(ServerState.Running, "losing the crash trace must not stop the server from starting");
    }

    [Fact]
    public async Task AListenerThatThrows_DoesNotBreakTheSupervisor()
    {
        var instance = _h.AddInstance("one");
        _h.Supervisor.StatusChanged += (_, _) => throw new InvalidOperationException("a buggy view model");

        var status = await _h.Supervisor.StartAsync(instance);

        status.State.Should().Be(ServerState.Running);
    }

    [Fact]
    public async Task ARestartThatFails_LeavesTheServerFailed_InsteadOfLoopingOrThrowing()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance, new ServerStartOptions { RestartOnCrash = true, MaxRestarts = 3 });
        _h.Spawner.Factory = _ => throw new InvalidOperationException("The disk is gone");

        _h.Spawner.Last.Exit(1);
        await SupervisorHarness.WaitForAsync(
            () => _h.Supervisor.GetStatus(instance.Id).FailureReason?.Contains("disk is gone", StringComparison.Ordinal) == true,
            "failed restart"
        );

        _h.Supervisor.GetStatus(instance.Id).State.Should().Be(ServerState.Failed);
    }

    [Fact]
    public async Task Stop_KillsTheServerAtOnce_WhenTheCallerCancelsDuringTheGracePeriod()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);
        using var cancellation = new CancellationTokenSource();
        _h.Spawner.Last.OnRequestStop = _ => cancellation.Cancel();

        var status = await _h.Supervisor.StopAsync(instance.Id, TimeSpan.FromMinutes(1), cancellation.Token);

        _h.Spawner.Last.WasKilled.Should().BeTrue("giving up waiting must not leave the server running");
        status.State.Should().Be(ServerState.Stopped);
    }

    [Fact]
    public async Task StoppingTheServer_KeepsTheGameHalfOfTheRecord()
    {
        // The pair is one record: the game outlives a server restart, and forgetting the whole record would lose it.
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);
        var server = _h.Runs.ReadAll().Single();

        _h.Runs.Save(server.WithGame(new RunProcess(9001, _h.Time.GetUtcNow(), "/srv/EscapeFromTarkov.exe", RunMechanism.SystemdScope)));

        await _h.Supervisor.StopAsync(instance.Id);

        var record = _h.Runs.ReadAll().Should().ContainSingle().Subject;
        record.Server.Should().BeNull();
        record.Game!.ProcessId.Should().Be(9001);
    }

    // ---- running ----

    [Fact]
    public async Task AServerThatDiesOnItsOwn_BecomesFailed_WithItsExitCodeAndLastWords()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);

        SupervisorHarness.Say(_h.Spawner.Starts[0], "A mod threw", "System.NullReferenceException");
        _h.Spawner.Last.Exit(134);
        await SupervisorHarness.WaitForAsync(() => _h.Supervisor.GetStatus(instance.Id).State == ServerState.Failed, "failed state");

        var status = _h.Supervisor.GetStatus(instance.Id);
        status.ExitCode.Should().Be(134);
        status.FailureReason.Should().Contain("exited on its own with code 134").And.Contain("System.NullReferenceException");
        _h.Runs.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public async Task ACrashedServer_IsNotRestarted_UnlessAskedTo()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);

        _h.Spawner.Last.Exit(1);
        await SupervisorHarness.WaitForAsync(() => _h.Supervisor.GetStatus(instance.Id).State == ServerState.Failed, "failed state");
        await Task.Delay(50);

        _h.Spawner.Starts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ACrashedServer_IsRestartedAFewTimes_WhenAskedTo_ThenLeftFailed()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance, new ServerStartOptions { RestartOnCrash = true, MaxRestarts = 2 });

        for (var crash = 1; crash <= 2; crash++)
        {
            var expected = crash + 1;
            _h.Spawner.Last.Exit(1);
            await SupervisorHarness.WaitForAsync(
                () => _h.Spawner.Children.Count == expected && _h.Supervisor.GetStatus(instance.Id).State == ServerState.Running,
                $"restart number {crash}"
            );
        }

        _h.Supervisor.GetStatus(instance.Id).Restarts.Should().Be(2);

        _h.Spawner.Last.Exit(1);
        await SupervisorHarness.WaitForAsync(() => _h.Supervisor.GetStatus(instance.Id).State == ServerState.Failed, "failed for good");
        await Task.Delay(50);

        _h.Spawner.Children.Should().HaveCount(3, "the first start and two restarts, then it gave up");
    }

    [Fact]
    public async Task AStopThatTheUserAsked_IsNeverMistakenForACrash()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance, new ServerStartOptions { RestartOnCrash = true });

        await _h.Supervisor.StopAsync(instance.Id);
        await Task.Delay(50);

        _h.Supervisor.GetStatus(instance.Id).State.Should().Be(ServerState.Stopped);
        _h.Spawner.Children.Should().HaveCount(1);
    }

    [Fact]
    public async Task AStopAfterTheServerAlreadyCrashed_KeepsTheFailure_AndRestartsNothing()
    {
        // The order matters and this pins it: the crash is noticed first, so the instance stays "Failed" (the reason a user
        // needs) and a later Stop only cleans up the process, it does not pretend nothing happened nor start anything.
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);

        _h.Spawner.Last.Exit(134);
        await SupervisorHarness.WaitForAsync(
            () => _h.Supervisor.GetStatus(instance.Id).State == ServerState.Failed,
            "the crash is noticed"
        );

        var status = await _h.Supervisor.StopAsync(instance.Id);
        await Task.Delay(50);

        status.State.Should().Be(ServerState.Failed);
        status.FailureReason.Should().Contain("134");
        _h.Spawner.Children.Should().HaveCount(1);
    }

    [Fact]
    public async Task Stop_SaysStopped_WhenItRunsWhileTheServerIsStillStarting()
    {
        var instance = _h.AddInstance("one");
        var asked = new TaskCompletionSource();
        _h.Client.PingBehavior = async token =>
        {
            asked.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return false;
        };

        var starting = _h.Supervisor.StartAsync(instance);
        await asked.Task;
        var status = await _h.Supervisor.StopAsync(instance.Id);

        status.State.Should().Be(ServerState.Stopped, "a start that never finished leaves nothing to report as failed");
        await FluentActions.Awaiting(() => starting).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TwoStopsAtOnce_NeitherReportsACrashNorRestarts()
    {
        // One stop holds the gate and waits out its grace period; the server dies meanwhile, so the monitor queues behind
        // that stop, and a second stop queues after the monitor. The monitor must not call the exit a crash just because
        // it got the gate while the first stop was still finishing.
        var crashed = 0;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var instance = _h.AddInstance($"double-stop-{attempt}");
            await _h.Supervisor.StartAsync(instance, new ServerStartOptions { RestartOnCrash = true, MaxRestarts = 2 });
            var child = _h.Spawner.Last;
            _h.Supervisor.StatusChanged += (_, status) =>
            {
                if (status.State == ServerState.Failed)
                {
                    Interlocked.Increment(ref crashed);
                }
            };

            child.OnRequestStop = _ => { };
            var first = _h.Supervisor.StopAsync(instance.Id, TimeSpan.FromSeconds(5));
            await Task.Delay(20);
            child.Exit(0);
            var second = _h.Supervisor.StopAsync(instance.Id, TimeSpan.FromSeconds(5));

            await Task.WhenAll(first, second);

            (await first).State.Should().Be(ServerState.Stopped);
            (await second).State.Should().Be(ServerState.Stopped);
        }

        crashed.Should().Be(0, "the exit was asked for by a stop, so it is not a failure and must not trigger a restart");
        _h.Spawner.Children.Should().HaveCount(10);
    }

    [Fact]
    public async Task RecentOutput_KeepsTheNewestLines_AndNeverGrowsWithoutBound()
    {
        var instance = _h.AddInstance("one");
        await _h.Supervisor.StartAsync(instance);

        for (var number = 1; number <= 3000; number++)
        {
            SupervisorHarness.Say(_h.Spawner.Starts[0], $"line {number}");
        }

        _h.Supervisor.RecentOutput(instance.Id)
            .Select(line => line.Text)
            .Should()
            .HaveCount(200)
            .And.EndWith("line 3000")
            .And.NotContain("line 2800");
        _h.Supervisor.RecentOutput(instance.Id, 5000).Should().HaveCount(2000, "the buffer is bounded");
        _h.Supervisor.RecentOutput(Guid.NewGuid()).Should().BeEmpty();
    }
}
