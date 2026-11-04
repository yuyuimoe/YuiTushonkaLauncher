using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Starts the processes of a run and detaches them, so the launcher's own death does not take them
/// (see <c>docs/plan/04-decisions.md#d22-the-server-and-the-game-are-detached-and-adopted</c>): a systemd user
/// scope when there is one, otherwise a new session through <c>setsid</c>, otherwise a plain child. Windows uses a
/// job object the launcher does not own, plus <c>CREATE_BREAKAWAY_FROM_JOB</c>.
/// </summary>
public sealed class ChildProcessSpawner : IChildProcessSpawner, IDisposable, IAsyncDisposable
{
    private readonly IPlatformInfo _platform;

    private readonly ILogger<ChildProcessSpawner> _logger;

    private readonly SpawnThread _spawnThread = new();

    private readonly object _jobLock = new();

    private IDisposable? _job;

    private readonly IExecutableFinder _finder;

    private readonly IProcessRunner _runner;

    private readonly Lazy<DetachStrategy> _strategy;

    public ChildProcessSpawner(IPlatformInfo platform, IExecutableFinder finder, IProcessRunner runner, ILogger<ChildProcessSpawner> logger)
    {
        _platform = platform;
        _logger = logger;
        _finder = finder;
        _runner = runner;

        // Decided on first use, never here: deciding runs `systemd-run --user --scope true`, and a constructor that waits
        // on a child process is a constructor that can deadlock the UI thread it was called from.
        _strategy = new Lazy<DetachStrategy>(Choose);
    }

    /// <summary>What this machine can do, decided once from what is installed.</summary>
    public DetachStrategy Strategy
    {
        get { return _strategy.Value; }
    }

    private DetachStrategy Choose()
    {
        var systemdRun = _platform.IsLinux ? _finder.Find("systemd-run") : null;
        var setsid = _platform.IsLinux ? _finder.Find("setsid") : null;

        var strategy = DetachStrategy.Choose(
            _platform.IsLinux,
            systemdRun,
            systemdRun is not null && SystemdUserIsRunning(_runner, systemdRun),
            setsid
        );

        if (_platform.IsLinux && strategy.Mechanism == RunMechanism.None)
        {
            _logger.LogWarning("Neither 'systemd-run' nor 'setsid' is available: a process started now dies with the launcher.");
        }

        return strategy;
    }

    /// <inheritdoc />
    public RunMechanism Mechanism
    {
        get { return Strategy.Mechanism; }
    }

    /// <inheritdoc />
    public IChildProcess Start(ChildProcessStart request)
    {
        // The wrapper is started from this one thread and never handed to the pool: a scope or a session inherits the
        // thread that created it, and a pool thread can be retired mid-flight.
        return _spawnThread.RunAsync(() => StartOnSpawnThread(request)).GetAwaiter().GetResult();
    }

    /// <summary>Asks systemd whether a user instance answers; a missing user manager means scopes are not usable.</summary>
    private static bool SystemdUserIsRunning(IProcessRunner runner, string systemdRun)
    {
        try
        {
            var result = runner
                .RunAsync(
                    new LaunchCommand { FileName = systemdRun, Arguments = ["--user", "--scope", "--quiet", "true"] },
                    timeout: TimeSpan.FromSeconds(10)
                )
                .GetAwaiter()
                .GetResult();

            return result.ExitCode == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _spawnThread.Dispose();
        _job?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>The thread the last child was created on; tests check it is the dedicated one and not a pool thread.</summary>
    internal Thread? LastSpawnerThread { get; private set; }

    private ManagedChild StartOnSpawnThread(ChildProcessStart request)
    {
        LastSpawnerThread = Thread.CurrentThread;
        var command = request.Command;
        var fileName = command.FileName;
        var arguments = command.Arguments.ToList();

        // A named unit is what makes the process findable after it forks: the pid of a wrapper is not the pid of the game.
        var unit = Strategy.Mechanism == RunMechanism.SystemdScope ? $"ytt-{Guid.NewGuid():n}" : null;

        if (Strategy.Program is { } wrapper)
        {
            arguments.Insert(0, fileName);
            arguments.InsertRange(0, Strategy.Arguments);

            if (unit is not null)
            {
                arguments.InsertRange(0, [$"--unit={unit}"]);
            }

            fileName = wrapper;
        }

        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,

            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (command.WorkingDirectory is not null)
        {
            info.WorkingDirectory = command.WorkingDirectory;
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in command.Environment)
        {
            info.Environment[name] = value;
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException($"'{command.FileName}' did not start.");

        // A server that asks "press any key" must find stdin closed, or it would wait for a key nobody can press.
        process.StandardInput.Close();

        if (_platform.IsWindows && OperatingSystem.IsWindows())
        {
            AssignToJob(process);
        }

        return new ManagedChild(process, request.OnOutput) { Mechanism = Strategy.Mechanism, Scope = unit };
    }

    [SupportedOSPlatform("windows")]
    private void AssignToJob(Process process)
    {
        try
        {
            WindowsJobObject job;

            lock (_jobLock)
            {
                job = (WindowsJobObject)(_job ??= new WindowsJobObject());
            }

            job.Assign(process);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(exception, "The server could not be put in a job object: it can outlive a launcher that crashes.");
        }
    }
}

/// <summary>A started process with its output pumped to a callback.</summary>
internal sealed class ManagedChild : IChildProcess
{
    /// <summary>How this child was detached; the store records it so the UI can explain what outlives what.</summary>
    public RunMechanism Mechanism { get; init; }

    /// <summary>The systemd unit, when the scope mechanism was used.</summary>
    public string? Scope { get; init; }

    private readonly Process _process;

    private volatile bool _hasExited;

    public ManagedChild(Process process, Action<ProcessLine>? onOutput)
    {
        _process = process;
        ProcessId = process.Id;

        // A program that exits at once (a server that fails on its first line) has no readable StartTime any more. The record
        // exists so a later launcher can recognise this process; "about now" is enough, and losing it must not lose the exit.
        try
        {
            StartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StartedAt = DateTimeOffset.UtcNow;
        }

        var readers = Task.WhenAll(Pump(process.StandardOutput, false, onOutput), Pump(process.StandardError, true, onOutput));
        Exited = WaitAsync(readers);
    }

    public int ProcessId { get; }

    public DateTimeOffset StartedAt { get; }

    public bool HasExited
    {
        get { return _hasExited; }
    }

    public Task<int> Exited { get; }

    public void RequestStop()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // A console process without a window has no clean request; Kill after the grace period is the only stop.
                return;
            }

            Signal(15);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    public void Kill()
    {
        try
        {
            if (!_hasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private void Signal(int signal)
    {
        if (!_hasExited)
        {
            Interop.Kill(ProcessId, signal);
        }
    }

    private async Task<int> WaitAsync(Task readers)
    {
        await _process.WaitForExitAsync();
        var code = _process.ExitCode;
        _hasExited = true;

        // A daemon the process started can hold the pipes open; do not wait for them forever.
        await Task.WhenAny(readers, Task.Delay(TimeSpan.FromSeconds(3)));

        _process.Dispose();
        return code;
    }

    private static async Task Pump(StreamReader reader, bool isError, Action<ProcessLine>? onOutput)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            onOutput?.Invoke(new ProcessLine(isError, line));
        }
    }
}
