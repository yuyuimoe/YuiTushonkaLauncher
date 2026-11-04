using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Server;

public enum ServerState
{
    Stopped,
    Starting,
    Running,
    Stopping,

    /// <summary>The server did not come up or died unexpectedly; <see cref="ServerStatus.FailureReason" /> says why.</summary>
    Failed,
}

/// <summary>The state of one instance's server.</summary>
public sealed record ServerStatus
{
    public required Guid InstanceId { get; init; }

    public ServerState State { get; init; }

    public int? ProcessId { get; init; }

    public ServerEndpoint? Endpoint { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>How long the server took to answer <c>ping</c>.</summary>
    public TimeSpan? ReadyAfter { get; init; }

    public int? ExitCode { get; init; }

    /// <summary>A sentence a person can act on; set when <see cref="State" /> is <see cref="ServerState.Failed" />.</summary>
    public string? FailureReason { get; init; }

    public int Restarts { get; init; }
}

/// <summary>One line the server wrote.</summary>
public sealed record ServerOutputLine(DateTimeOffset At, bool IsError, string Text);

/// <summary>The checked plan for starting a server: nothing is started to produce it.</summary>
public sealed record ServerStartPlan
{
    public required SptServerLocation Location { get; init; }

    public Runtime.LaunchCommand? Command { get; init; }

    public IReadOnlyList<ServerProblem> Problems { get; init; } = [];

    public bool CanStart
    {
        get { return Command is not null && Problems.All(problem => !problem.IsError); }
    }

    public IEnumerable<ServerProblem> Errors
    {
        get { return Problems.Where(problem => problem.IsError); }
    }
}

public sealed record ServerStartOptions
{
    /// <summary>How long to wait for <c>ping</c> before giving up; a slow machine needs more than a fast one.</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Start the server again when it dies on its own. Off by default: a crash loop hides the cause.</summary>
    public bool RestartOnCrash { get; init; }

    public int MaxRestarts { get; init; } = 3;
}

/// <summary>A server a previous launcher left running (it crashed and could not clean up).</summary>
public sealed record StaleServer(Guid InstanceId, int ProcessId, string ExecutablePath, int Port, DateTimeOffset StartedAt);

/// <summary>Raised when a server cannot be started or stopped; the message is meant to be shown as is.</summary>
public sealed class ServerStartException(string message, IReadOnlyList<ServerProblem>? problems = null) : Exception(message)
{
    public IReadOnlyList<ServerProblem> Problems { get; } = problems ?? [];
}

/// <summary>Owns the server processes: at most one per instance, never left behind.</summary>
public interface ISptServerSupervisor
{
    /// <summary>Plans a start: locates the server, checks the runtime and the port. Starts nothing.</summary>
    Task<ServerStartPlan> PlanAsync(Instance instance, CancellationToken cancellationToken = default);

    /// <summary>Starts the server and returns once it answers <c>ping</c>, or throws with the reason.</summary>
    Task<ServerStatus> StartAsync(Instance instance, ServerStartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Stops the server: a graceful request first, then a kill after <paramref name="gracePeriod" />.</summary>
    Task<ServerStatus> StopAsync(Guid instanceId, TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default);

    ServerStatus GetStatus(Guid instanceId);

    /// <summary>The newest lines the server wrote, oldest first; the buffer is bounded.</summary>
    IReadOnlyList<ServerOutputLine> RecentOutput(Guid instanceId, int maxLines = 200);

    /// <summary>Raised on every state change.</summary>
    event EventHandler<ServerStatus>? StatusChanged;

    /// <summary>Servers left over from a launcher that died without cleaning up.</summary>
    Task<IReadOnlyList<StaleServer>> FindStaleAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops every stale server; returns how many were stopped.</summary>
    Task<int> ReapStaleAsync(CancellationToken cancellationToken = default);
}
