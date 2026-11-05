namespace YuiTushonkaLauncher.Core.Launch;

/// <summary>What the launch button is doing, in the words the UI shows.</summary>
public enum LaunchButtonState
{
    Idle,

    PreFlight,

    Patching,

    StartingServer,

    WaitingForServer,

    Launching,

    /// <summary>Both processes are running and detached.</summary>
    Running,

    Stopping,

    Failed,

    /// <summary>Stopped on request before the game started.</summary>
    Cancelled,
}

/// <summary>
/// The state the launch button is in, driven only by <see cref="LaunchProgress" />. Kept apart from the window so the
/// rule "cancel is only possible until the game starts" is testable without a UI.
/// </summary>
public sealed class LaunchSession
{
    private readonly object _lock = new();
    private LaunchStep? _furthest;

    /// <summary>Raised on every change, with the state that resulted.</summary>
    public event EventHandler<LaunchButtonState>? Changed;

    public LaunchButtonState State { get; private set; } = LaunchButtonState.Idle;

    /// <summary>The last sentence a step produced, for the line under the button.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Patching progress, when the current step reports it.</summary>
    public double? Progress { get; private set; }

    /// <summary>
    /// True until the game is about to start: cancelling afterwards would not stop an action, it would kill a detached
    /// process, which is a Stop and a different button.
    /// </summary>
    public bool CanCancel
    {
        get
        {
            lock (_lock)
            {
                return State
                    is LaunchButtonState.PreFlight
                        or LaunchButtonState.Patching
                        or LaunchButtonState.StartingServer
                        or LaunchButtonState.WaitingForServer
                        or LaunchButtonState.Launching;
            }
        }
    }

    /// <summary>True once the pair is running: from here the useful action is stopping it.</summary>
    public bool CanStop
    {
        get
        {
            lock (_lock)
            {
                return State is LaunchButtonState.Running;
            }
        }
    }

    /// <summary>True when a launch can be started (nothing is in flight, or the last one ended).</summary>
    public bool CanLaunch
    {
        get
        {
            lock (_lock)
            {
                return State
                    is LaunchButtonState.Idle
                        or LaunchButtonState.Running
                        or LaunchButtonState.Failed
                        or LaunchButtonState.Cancelled;
            }
        }
    }

    /// <summary>Applies one step event.</summary>
    public void Apply(LaunchProgress progress)
    {
        lock (_lock)
        {
            // Reports are delivered through IProgress<T>, which posts: a step can arrive after a later one. A failure or a
            // cancellation is always shown; a plain step behind the furthest one so far is stale and is dropped.
            if (
                progress.Outcome is LaunchStepOutcome.Running or LaunchStepOutcome.Succeeded
                && _furthest is { } furthest
                && progress.Step < furthest
            )
            {
                return;
            }

            _furthest = _furthest is { } current && current > progress.Step ? current : progress.Step;
            Message = progress.Message;
            Progress = progress.Progress;

            State = progress.Outcome switch
            {
                LaunchStepOutcome.Cancelled => LaunchButtonState.Cancelled,
                LaunchStepOutcome.Failed => LaunchButtonState.Failed,
                _ => Map(progress.Step, progress.Outcome),
            };
        }

        Changed?.Invoke(this, State);
    }

    /// <summary>A launch that never started because the pre-flight refused.</summary>
    public void Fail(string message)
    {
        lock (_lock)
        {
            Message = message;
            Progress = null;
            State = LaunchButtonState.Failed;
        }

        Changed?.Invoke(this, State);
    }

    /// <summary>The user asked to stop the running pair.</summary>
    public void BeginStop()
    {
        lock (_lock)
        {
            if (State == LaunchButtonState.Running)
            {
                State = LaunchButtonState.Stopping;
            }
        }

        Changed?.Invoke(this, State);
    }

    /// <summary>The pair is gone; the button is ready again.</summary>
    public void Stopped()
    {
        lock (_lock)
        {
            State = LaunchButtonState.Idle;
        }

        Changed?.Invoke(this, State);
    }

    private static LaunchButtonState Map(LaunchStep step, LaunchStepOutcome outcome)
    {
        return step switch
        {
            LaunchStep.PreFlight => outcome == LaunchStepOutcome.Succeeded ? LaunchButtonState.Patching : LaunchButtonState.PreFlight,
            LaunchStep.PatchCheck => outcome == LaunchStepOutcome.Succeeded ? LaunchButtonState.StartingServer : LaunchButtonState.Patching,
            LaunchStep.ServerStart => outcome == LaunchStepOutcome.Succeeded
                ? LaunchButtonState.WaitingForServer
                : LaunchButtonState.StartingServer,
            LaunchStep.Ready => outcome == LaunchStepOutcome.Succeeded ? LaunchButtonState.Launching : LaunchButtonState.WaitingForServer,
            LaunchStep.GameStart => outcome == LaunchStepOutcome.Succeeded ? LaunchButtonState.Launching : LaunchButtonState.Launching,
            _ => outcome == LaunchStepOutcome.Succeeded ? LaunchButtonState.Running : LaunchButtonState.Launching,
        };
    }
}
