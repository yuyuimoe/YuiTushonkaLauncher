using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Starts child processes that end with the launcher. Linux: <c>setpriv --pdeathsig TERM</c> in front of the
/// command, started from a thread that never ends (<see cref="SpawnThread" />); the child gets SIGTERM, so a
/// server still saves, even when the launcher is killed. Windows: a job object that kills on close.
/// </summary>
public sealed class ChildProcessSpawner : IChildProcessSpawner, IDisposable, IAsyncDisposable
{
    private readonly IPlatformInfo _platform;

    private readonly ILogger<ChildProcessSpawner> _logger;

    private readonly string? _setpriv;

    private readonly SpawnThread _spawnThread = new();

    private readonly object _jobLock = new();

    private IDisposable? _job;

    public ChildProcessSpawner(IPlatformInfo platform, IExecutableFinder finder, ILogger<ChildProcessSpawner> logger)
    {
        _platform = platform;
        _logger = logger;
        _setpriv = platform.IsLinux ? finder.Find("setpriv") : null;
    }

    /// <inheritdoc />
    public bool TiesChildrenToLauncher
    {
        get { return _platform.IsWindows || _setpriv is not null; }
    }

    /// <inheritdoc />
    public IChildProcess Start(ChildProcessStart request)
    {
        // Wait for the spawner thread, not the pool: the child's lifetime is tied to the thread that creates it.
        return _spawnThread.RunAsync(() => StartOnSpawnThread(request)).GetAwaiter().GetResult();
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

        if (_setpriv is not null)
        {
            arguments.Insert(0, fileName);
            arguments.InsertRange(0, ["--pdeathsig", "TERM"]);
            fileName = _setpriv;
        }
        else if (_platform.IsLinux)
        {
            _logger.LogWarning("'setpriv' was not found: a server started now can outlive a launcher that crashes.");
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

        return new ManagedChild(process, request.OnOutput);
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
    private readonly Process _process;

    private volatile bool _hasExited;

    public ManagedChild(Process process, Action<ProcessLine>? onOutput)
    {
        _process = process;
        ProcessId = process.Id;
        StartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);

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
