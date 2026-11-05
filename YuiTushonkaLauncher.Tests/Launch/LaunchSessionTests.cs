using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Launch;

namespace YuiTushonkaLauncher.Tests.Launch;

public sealed class LaunchSessionTests
{
    private readonly LaunchSession _session = new();

    private static LaunchProgress Progress(LaunchStep step, LaunchStepOutcome outcome = LaunchStepOutcome.Running, string message = "")
    {
        return new LaunchProgress(step, outcome, message);
    }

    private void Run(params LaunchProgress[] steps)
    {
        foreach (var step in steps)
        {
            _session.Apply(step);
        }
    }

    [Fact]
    public void ANewSession_IsIdle_AndCanLaunch()
    {
        _session.State.Should().Be(LaunchButtonState.Idle);
        _session.CanLaunch.Should().BeTrue();
        _session.CanCancel.Should().BeFalse();
        _session.CanStop.Should().BeFalse();
    }

    [Fact]
    public void TheStepsDriveTheStateInTheOrderThePipelineEmitsThem()
    {
        var states = new List<LaunchButtonState>();
        _session.Changed += (_, state) => states.Add(state);

        Run(
            Progress(LaunchStep.PreFlight),
            Progress(LaunchStep.PreFlight, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.PatchCheck),
            Progress(LaunchStep.PatchCheck, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.ServerStart),
            Progress(LaunchStep.ServerStart, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.Ready, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.GameStart),
            Progress(LaunchStep.GameStart, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.Detached, LaunchStepOutcome.Succeeded)
        );

        states
            .Should()
            .Equal(
                LaunchButtonState.PreFlight,
                LaunchButtonState.Patching,
                LaunchButtonState.Patching,
                LaunchButtonState.StartingServer,
                LaunchButtonState.StartingServer,
                LaunchButtonState.WaitingForServer,
                LaunchButtonState.Launching,
                LaunchButtonState.Launching,
                LaunchButtonState.Launching,
                LaunchButtonState.Running
            );
        _session.CanStop.Should().BeTrue();
        _session.CanCancel.Should().BeFalse("the game is detached; the action is Stop, not cancel");
    }

    [Fact]
    public void CancelIsPossibleUntilTheGameStarts_AndNotAfter()
    {
        Run(Progress(LaunchStep.PreFlight));
        _session.CanCancel.Should().BeTrue();

        Run(Progress(LaunchStep.PreFlight, LaunchStepOutcome.Succeeded), Progress(LaunchStep.PatchCheck));
        _session.CanCancel.Should().BeTrue();

        Run(Progress(LaunchStep.PatchCheck, LaunchStepOutcome.Succeeded), Progress(LaunchStep.ServerStart));
        _session.CanCancel.Should().BeTrue();

        Run(Progress(LaunchStep.ServerStart, LaunchStepOutcome.Succeeded), Progress(LaunchStep.Ready));
        _session.CanCancel.Should().BeTrue();

        Run(Progress(LaunchStep.Ready, LaunchStepOutcome.Succeeded), Progress(LaunchStep.GameStart));
        _session.CanCancel.Should().BeTrue("the game has not started yet");

        Run(Progress(LaunchStep.GameStart, LaunchStepOutcome.Succeeded), Progress(LaunchStep.Detached, LaunchStepOutcome.Succeeded));
        _session.CanCancel.Should().BeFalse();
        _session.CanStop.Should().BeTrue();
    }

    [Fact]
    public void ItReportsTheLastMessageAndThePatchProgress()
    {
        _session.Apply(new LaunchProgress(LaunchStep.PatchCheck, LaunchStepOutcome.Running, "Assembly-CSharp.dll 40%", 0.4));

        _session.State.Should().Be(LaunchButtonState.Patching);
        _session.Message.Should().Be("Assembly-CSharp.dll 40%");
        _session.Progress.Should().Be(0.4);
    }

    [Fact]
    public void AFailedStepLeavesTheButtonFailed_WithTheReason()
    {
        Run(
            Progress(LaunchStep.PreFlight, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.PatchCheck, LaunchStepOutcome.Failed, "The delta is corrupt.")
        );

        _session.State.Should().Be(LaunchButtonState.Failed);
        _session.Message.Should().Be("The delta is corrupt.");
        _session.CanLaunch.Should().BeTrue("a failed launch can be tried again");
        _session.CanStop.Should().BeFalse();
    }

    [Fact]
    public void ACancelledLaunchLeavesACancelledState_AndCanBeRetried()
    {
        Run(Progress(LaunchStep.PreFlight), Progress(LaunchStep.PreFlight, LaunchStepOutcome.Cancelled, "The launch was cancelled."));

        _session.State.Should().Be(LaunchButtonState.Cancelled);
        _session.CanLaunch.Should().BeTrue();
        _session.CanCancel.Should().BeFalse();
    }

    [Fact]
    public void ARefusedPreFlight_FailsWithItsMessage()
    {
        _session.Fail("No profile was chosen.");

        _session.State.Should().Be(LaunchButtonState.Failed);
        _session.Message.Should().Be("No profile was chosen.");
    }

    [Fact]
    public void StoppingGoesThroughItsOwnState_AndEndsIdle()
    {
        Run(
            Progress(LaunchStep.PreFlight, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.PatchCheck, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.ServerStart, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.Ready, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.GameStart, LaunchStepOutcome.Succeeded),
            Progress(LaunchStep.Detached, LaunchStepOutcome.Succeeded)
        );

        _session.BeginStop();

        _session.State.Should().Be(LaunchButtonState.Stopping);
        _session.CanStop.Should().BeFalse();
        _session.CanLaunch.Should().BeFalse("stopping is in flight");

        _session.Stopped();

        _session.State.Should().Be(LaunchButtonState.Idle);
        _session.CanLaunch.Should().BeTrue();
    }

    [Fact]
    public void BeginStopIsIgnored_WhenNothingRuns()
    {
        _session.BeginStop();

        _session.State.Should().Be(LaunchButtonState.Idle);
    }
}
