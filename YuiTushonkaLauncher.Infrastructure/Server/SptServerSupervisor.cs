using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Runs the SPT servers: at most one per instance, a port nobody else uses, readiness by polling, a graceful stop, and a
/// server that outlives the launcher ([D22](../../docs/plan/04-decisions.md#d22-the-server-and-the-game-are-detached-and-adopted));
/// a leftover from a crash is found and stopped from its pid record.
/// </summary>
public sealed class SptServerSupervisor : ISptServerSupervisor, IDisposable, IAsyncDisposable
{
    private const int MaxBufferedLines = 2000;

    private static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan StaleGrace = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    private readonly ISptServerLocator _locator;

    private readonly IDotNetRuntimeProbe _dotNet;

    private readonly IChildProcessSpawner _spawner;

    private readonly ISptServerClientFactory _clients;

    private readonly IPortProbe _ports;

    private readonly IRuntimeLauncher _runtimeLauncher;

    private readonly IRuntimeProfileService _profiles;

    private readonly IInstanceService _instances;

    private readonly IProcessInspector _inspector;

    private readonly IInstanceRunStore _runStore;

    private readonly IPlatformInfo _platform;

    private readonly TimeProvider _time;

    private readonly ILogger<SptServerSupervisor> _logger;

    public SptServerSupervisor(
        ISptServerLocator locator,
        IDotNetRuntimeProbe dotNet,
        IChildProcessSpawner spawner,
        ISptServerClientFactory clients,
        IPortProbe ports,
        IRuntimeLauncher runtimeLauncher,
        IRuntimeProfileService profiles,
        IInstanceService instances,
        IProcessInspector inspector,
        IInstanceRunStore runStore,
        IPlatformInfo platform,
        TimeProvider time,
        ILogger<SptServerSupervisor> logger
    )
    {
        _locator = locator;
        _dotNet = dotNet;
        _spawner = spawner;
        _clients = clients;
        _ports = ports;
        _runtimeLauncher = runtimeLauncher;
        _profiles = profiles;
        _instances = instances;
        _inspector = inspector;
        _runStore = runStore;
        _platform = platform;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<ServerStatus>? StatusChanged;

    /// <inheritdoc />
    public async Task<ServerStartPlan> PlanAsync(Instance instance, CancellationToken cancellationToken = default)
    {
        var location = _locator.Locate(instance);
        var problems = new List<ServerProblem>(location.Problems);
        var current = _entries.TryGetValue(instance.Id, out var entry) ? entry.Status : null;

        if (current?.State is ServerState.Starting or ServerState.Running or ServerState.Stopping)
        {
            problems.Add(
                Error(
                    ServerProblemCode.AlreadyRunning,
                    $"The server of '{instance.Name}' is already running (process {current.ProcessId}, port {location.Endpoint.Port})."
                )
            );
        }

        if (location.ExecutablePath is not null)
        {
            await CheckFrameworksAsync(location, problems, cancellationToken);
        }

        if (current?.State is not (ServerState.Starting or ServerState.Running or ServerState.Stopping))
        {
            await CheckPortAsync(instance, location, problems, cancellationToken);
        }

        if (_spawner.Mechanism == RunMechanism.None)
        {
            problems.Add(
                new ServerProblem(
                    RuntimeProblemSeverity.Warning,
                    ServerProblemCode.DetachUnavailable,
                    "This machine offers neither 'systemd-run' nor 'setsid', so the server cannot be detached: it will die with the launcher."
                )
            );
        }

        LaunchCommand? command = null;

        if (location.ExecutablePath is not null)
        {
            var profile = await _profiles.ResolveForInstanceAsync(instance.Id, cancellationToken);
            var launch = _runtimeLauncher.Plan(
                instance,
                profile,
                new RuntimeLaunchRequest
                {
                    Program = location.ExecutablePath,
                    WorkingDirectory = location.WorkingDirectory,
                    Target = ProgramTarget.NativeProgram,
                }
            );

            command = launch.Command;
            problems.AddRange(
                launch.Problems.Select(problem => new ServerProblem(
                    problem.Severity,
                    ServerProblemCode.RuntimeProfileInvalid,
                    problem.Message,
                    problem.Path
                ))
            );
        }

        return new ServerStartPlan
        {
            Location = location,
            Command = command,
            Problems = problems,
        };
    }

    /// <inheritdoc />
    public async Task<ServerStatus> StartAsync(
        Instance instance,
        ServerStartOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var entry = _entries.GetOrAdd(instance.Id, id => new Entry(id, instance.Name));
        await entry.Gate.WaitAsync(cancellationToken);

        try
        {
            entry.Cancellation?.Dispose();
            entry.Cancellation = new CancellationTokenSource();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Cancellation.Token);
            return await StartCoreAsync(entry, instance, options ?? new ServerStartOptions(), 0, linked.Token);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServerStatus> StopAsync(Guid instanceId, TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(instanceId, out var entry))
        {
            return new ServerStatus { InstanceId = instanceId, State = ServerState.Stopped };
        }

        // Cancel first: a start or a restart that is still waiting for ping must give up before the gate can be had.
        entry.Cancellation?.Cancel();

        await entry.Gate.WaitAsync(cancellationToken);

        try
        {
            var child = entry.Child;

            if (child is null || child.HasExited)
            {
                entry.Child = null;
                ForgetServer(instanceId);

                if (entry.Status.State is ServerState.Starting or ServerState.Running or ServerState.Stopping)
                {
                    Publish(entry, entry.Status with { State = ServerState.Stopped, ProcessId = null });
                }

                return entry.Status;
            }

            Publish(entry, entry.Status with { State = ServerState.Stopping });
            child.RequestStop();

            // Cancelling the token cuts the grace period short: the delay ends early and the server is killed. WhenAny does not
            // rethrow the cancellation, which is right here, because a caller that gives up waiting still must not leave a server behind.
            var finished =
                await Task.WhenAny(child.Exited, Task.Delay(gracePeriod ?? DefaultGrace, _time, cancellationToken)) == child.Exited;

            if (!finished)
            {
                _logger.LogWarning("The server of '{Instance}' did not stop on request and was killed", entry.InstanceName);
                child.Kill();
                await WaitBoundedAsync(child.Exited);
            }

            var exitCode = child.Exited.IsCompletedSuccessfully ? child.Exited.Result : (int?)null;
            entry.Child = null;
            ForgetServer(instanceId);
            Publish(entry, entry.Status with { State = ServerState.Stopped, ProcessId = null, ExitCode = exitCode });
            return entry.Status;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <inheritdoc />
    public ServerStatus GetStatus(Guid instanceId)
    {
        return _entries.TryGetValue(instanceId, out var entry)
            ? entry.Status
            : new ServerStatus { InstanceId = instanceId, State = ServerState.Stopped };
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerOutputLine> RecentOutput(Guid instanceId, int maxLines = 200)
    {
        return _entries.TryGetValue(instanceId, out var entry) ? entry.Lines.Last(maxLines) : [];
    }

    public void Dispose()
    {
        // Nothing is stopped here on purpose ([D22]): the server is detached, so closing the launcher must leave it
        // running (the user is told by the close dialog, and --stale finds it if the launcher dies). The entry only
        // releases what it holds in memory.
        foreach (var entry in _entries.Values)
        {
            entry.Cancellation?.Dispose();
            entry.Gate.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<ServerStatus> StartCoreAsync(
        Entry entry,
        Instance instance,
        ServerStartOptions options,
        int restarts,
        CancellationToken cancellationToken
    )
    {
        if (entry.Status.State is ServerState.Starting or ServerState.Running or ServerState.Stopping)
        {
            throw new ServerStartException(
                $"The server of '{instance.Name}' is already running (process {entry.Status.ProcessId}).",
                [Error(ServerProblemCode.AlreadyRunning, "A server is already running for this instance.")]
            );
        }

        var plan = await PlanAsync(instance, cancellationToken);

        if (!plan.CanStart)
        {
            throw new ServerStartException(
                $"The server of '{instance.Name}' cannot start: " + string.Join(" ", plan.Errors.Select(problem => problem.Message)),
                plan.Problems
            );
        }

        var endpoint = plan.Location.Endpoint;
        entry.Lines.Clear();
        Publish(
            entry,
            new ServerStatus
            {
                InstanceId = entry.InstanceId,
                State = ServerState.Starting,
                Endpoint = endpoint,
                Restarts = restarts,
            }
        );

        IChildProcess child;

        try
        {
            child = _spawner.Start(
                new ChildProcessStart
                {
                    Command = plan.Command!,
                    RequiresConsole = NeedsAConsole(instance),
                    OnOutput = line => entry.Lines.Add(new ServerOutputLine(_time.GetUtcNow(), line.IsError, line.Text)),
                }
            );
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var reason = $"The server could not be started: {exception.Message}";
            Publish(entry, entry.Status with { State = ServerState.Failed, FailureReason = reason });
            throw new ServerStartException(reason);
        }

        entry.Child = child;
        WriteRecord(entry, instance, plan, child);
        Publish(entry, entry.Status with { ProcessId = child.ProcessId, StartedAt = child.StartedAt });

        var client = _clients.Create(endpoint);

        try
        {
            var ready = await ServerReadiness.WaitAsync(client, options.ReadyTimeout, () => !child.HasExited, _time, cancellationToken);
            Publish(entry, entry.Status with { State = ServerState.Running, ReadyAfter = ready, FailureReason = null });
            _ = MonitorAsync(entry, instance, child, options, restarts);
            return entry.Status;
        }
        catch (ServerNotReadyException exception)
        {
            var reason = await DescribeFailureAsync(entry, child, endpoint, exception);
            child.Kill();
            await WaitBoundedAsync(child.Exited);
            entry.Child = null;
            ForgetServer(entry.InstanceId);
            Publish(
                entry,
                entry.Status with
                {
                    State = ServerState.Failed,
                    ProcessId = null,
                    FailureReason = reason,
                    ExitCode = ExitCodeOf(child),
                }
            );
            throw new ServerStartException(reason);
        }
        catch (OperationCanceledException)
        {
            child.Kill();
            await WaitBoundedAsync(child.Exited);
            entry.Child = null;
            ForgetServer(entry.InstanceId);
            Publish(entry, entry.Status with { State = ServerState.Stopped, ProcessId = null });
            throw;
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    private async Task MonitorAsync(Entry entry, Instance instance, IChildProcess child, ServerStartOptions options, int restarts)
    {
        var code = await child.Exited;

        await entry.Gate.WaitAsync();

        try
        {
            // A stop owns the shutdown and reports it; only an exit nobody asked for is a failure. By the time a stop
            // releases the gate it has already forgotten the child, so this one check covers every order.
            if (!ReferenceEquals(entry.Child, child))
            {
                return;
            }

            entry.Child = null;
            ForgetServer(entry.InstanceId);

            var reason = $"The server exited on its own with code {code}." + LastLinesText(entry);
            _logger.LogWarning("Server of '{Instance}': {Reason}", entry.InstanceName, reason);
            Publish(entry, entry.Status with { State = ServerState.Failed, ProcessId = null, ExitCode = code, FailureReason = reason });

            if (
                options.RestartOnCrash
                && restarts < options.MaxRestarts
                && entry.Cancellation is { IsCancellationRequested: false } cancellation
            )
            {
                try
                {
                    await StartCoreAsync(entry, instance, options, restarts + 1, cancellation.Token);
                }
                catch (Exception exception) when (exception is ServerStartException or OperationCanceledException)
                {
                    _logger.LogWarning(exception, "Restarting the server of '{Instance}' failed", entry.InstanceName);
                }
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private async Task CheckFrameworksAsync(SptServerLocation location, List<ServerProblem> problems, CancellationToken cancellationToken)
    {
        if (location.Requirements.Count == 0)
        {
            return;
        }

        var installed = await _dotNet.ListInstalledAsync(cancellationToken);

        if (installed is null)
        {
            problems.Add(
                new ServerProblem(
                    RuntimeProblemSeverity.Warning,
                    ServerProblemCode.RuntimeUnverified,
                    "The installed .NET runtimes could not be listed ('dotnet' was not found or did not answer), so the server's requirement "
                        + $"({Describe(location.Requirements)}) was not checked."
                )
            );
            return;
        }

        foreach (var required in location.Requirements)
        {
            if (!installed.Any(candidate => Satisfies(candidate, required)))
            {
                var have = installed.Where(candidate => candidate.Name == required.Name).Select(candidate => candidate.Version).ToList();
                problems.Add(
                    Error(
                        ServerProblemCode.RuntimeMissing,
                        $"The server needs {required.Name} {required.Version} (or a newer {MajorOf(required.Version)}.x), and this machine has "
                            + (have.Count == 0 ? "none of it." : $"only {string.Join(", ", have)}.")
                            + $" Install the .NET {MajorOf(required.Version)} runtime"
                            + (required.Name.Contains("AspNetCore", StringComparison.Ordinal) ? " (it includes ASP.NET Core)." : ".")
                    )
                );
            }
        }
    }

    private async Task CheckPortAsync(
        Instance instance,
        SptServerLocation location,
        List<ServerProblem> problems,
        CancellationToken cancellationToken
    )
    {
        var port = location.Endpoint.Port;

        var runningElsewhere = _entries
            .Values.Where(other => other.InstanceId != instance.Id && other.Status.State is ServerState.Starting or ServerState.Running)
            .FirstOrDefault(other => other.Status.Endpoint?.Port == port);

        if (runningElsewhere is not null)
        {
            problems.Add(
                Error(
                    ServerProblemCode.PortUsedByRunningInstance,
                    $"Port {port} is in use by the running server of instance '{runningElsewhere.InstanceName}'. Give this instance another port (next free: {await SuggestAsync(port, cancellationToken)})."
                )
            );
            return;
        }

        if (!_ports.IsFree(port))
        {
            problems.Add(
                Error(
                    ServerProblemCode.PortBusy,
                    $"Port {port} is already in use by another program. Stop it, give this instance another port (next free: {await SuggestAsync(port, cancellationToken)}), "
                        + "or run --stale to check whether a server of a previous session is still running."
                )
            );
            return;
        }

        foreach (var other in await _instances.ListAsync(cancellationToken))
        {
            if (other.Id != instance.Id && _locator.Locate(other).Endpoint.Port == port)
            {
                problems.Add(
                    new ServerProblem(
                        RuntimeProblemSeverity.Warning,
                        ServerProblemCode.PortConfiguredByOtherInstance,
                        $"Instance '{other.Name}' is also configured for port {port}: the two cannot run at the same time."
                    )
                );
            }
        }
    }

    private async Task<int> SuggestAsync(int start, CancellationToken cancellationToken)
    {
        var configured = new HashSet<int>();

        foreach (var instance in await _instances.ListAsync(cancellationToken))
        {
            configured.Add(_locator.Locate(instance).Endpoint.Port);
        }

        return ServerPortService.FindFree(start + 1, _ports, configured);
    }

    private async Task<string> DescribeFailureAsync(
        Entry entry,
        IChildProcess child,
        ServerEndpoint endpoint,
        ServerNotReadyException exception
    )
    {
        if (!exception.ProcessExited)
        {
            return $"{exception.Message}{LastLinesText(entry)}";
        }

        // Give the last output a moment to arrive: the reason is usually in it.
        await WaitBoundedAsync(child.Exited);
        var text = string.Join('\n', entry.Lines.Last(40).Select(line => line.Text));

        // A busy port makes the server print the reason and then exit with code 0, so the exit code proves nothing.
        if (
            text.Contains("AddressAlreadyInUse", StringComparison.Ordinal)
            || text.Contains("Failed to start the web server", StringComparison.Ordinal)
        )
        {
            return $"The server could not listen on port {endpoint.Port}: another program (maybe another SPT server) is using it.{LastLinesText(entry)}";
        }

        return $"The server exited with code {ExitCodeOf(child)?.ToString() ?? "unknown"} before it answered ping.{LastLinesText(entry)}";
    }

    /// <summary>
    /// SPT 4.0.x on Windows calls <c>GetConsoleMode</c> on its standard output and aborts with "Unable to get console mode"
    /// when that handle is a pipe, so it is started with a console of its own instead of with captured output. Measured on
    /// 4.0.9; 4.1.x no longer does it (<c>docs/plan/03-verified-facts.md</c>).
    /// </summary>
    private bool NeedsAConsole(Instance instance)
    {
        return _platform.IsWindows && instance.SptVersion.Major == 4 && instance.SptVersion.Minor == 0;
    }

    private string LastLinesText(Entry entry)
    {
        var lines = entry.Lines.Last(8).Select(line => line.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToList();

        if (lines.Count > 0)
        {
            return " Last output: " + string.Join(" | ", lines);
        }

        // Nothing was captured because this server keeps its console: its own log file is where the reason is.
        return _platform.IsWindows
            ? " The server's console output is not captured on Windows; its own log file holds the reason (see Logs)."
            : string.Empty;
    }

    private void WriteRecord(Entry entry, Instance instance, ServerStartPlan plan, IChildProcess child)
    {
        try
        {
            var previous = _runStore.Find(entry.InstanceId);
            var run = (previous ?? new InstanceRun { InstanceId = entry.InstanceId, InstanceName = instance.Name }) with
            {
                InstanceName = instance.Name,
                LauncherProcessId = Environment.ProcessId,
            };

            _runStore.Save(
                run.WithServer(
                    new RunProcess(
                        child.ProcessId,
                        child.StartedAt,
                        plan.Location.ExecutablePath ?? string.Empty,
                        _spawner.Mechanism,
                        plan.Location.Endpoint.Port,
                        child.Scope
                    )
                )
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "The run record of '{Instance}' could not be written; a crash would leave no trace of it",
                instance.Name
            );
        }
    }

    /// <summary>Removes the server from the run record, keeping the game half if the game is still recorded.</summary>
    private void ForgetServer(Guid instanceId)
    {
        var run = _runStore.Find(instanceId);

        if (run is null)
        {
            return;
        }

        if (run.Game is null)
        {
            _runStore.Forget(instanceId);
            return;
        }

        _runStore.Save(run.WithServer(null));
    }

    private void Publish(Entry entry, ServerStatus status)
    {
        entry.Status = status;

        try
        {
            StatusChanged?.Invoke(this, status);
        }
        catch (Exception exception)
        {
            // A listener must not break the supervisor.
            _logger.LogWarning(exception, "A server status listener failed");
        }
    }

    private static int? ExitCodeOf(IChildProcess child)
    {
        return child.Exited.IsCompletedSuccessfully ? child.Exited.Result : null;
    }

    private static async Task WaitBoundedAsync(Task task)
    {
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
    }

    private static bool Satisfies(FrameworkRequirement installed, FrameworkRequirement required)
    {
        return installed.Name == required.Name
            && TryParseVersion(installed.Version, out var have)
            && TryParseVersion(required.Version, out var need)
            && have.Major == need.Major
            && have >= need;
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        var numeric = text.Split('-', '+')[0];
        return Version.TryParse(numeric, out version!);
    }

    private static string MajorOf(string version)
    {
        return TryParseVersion(version, out var parsed)
            ? parsed.Major.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : version;
    }

    private static string Describe(IEnumerable<FrameworkRequirement> requirements)
    {
        return string.Join(", ", requirements.Select(requirement => $"{requirement.Name} {requirement.Version}"));
    }

    private static ServerProblem Error(ServerProblemCode code, string message)
    {
        return new ServerProblem(RuntimeProblemSeverity.Error, code, message);
    }

    private sealed class Entry(Guid instanceId, string instanceName)
    {
        public Guid InstanceId { get; } = instanceId;

        public string InstanceName { get; } = instanceName;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public ServerOutputBuffer Lines { get; } = new(MaxBufferedLines);

        public volatile ServerStatus Status = new() { InstanceId = instanceId, State = ServerState.Stopped };

        public IChildProcess? Child { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }
    }
}
