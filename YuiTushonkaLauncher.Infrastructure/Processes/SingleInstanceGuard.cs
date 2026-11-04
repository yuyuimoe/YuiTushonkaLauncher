using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;

namespace YuiTushonkaLauncher.Infrastructure.Processes;

/// <summary>
/// Single-instance guard: a named mutex on Windows, an exclusively-opened lock file elsewhere
/// (.NET maps <see cref="FileShare.None" /> to <c>flock</c> on Unix).
/// </summary>
public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    private const string MutexName = @"Local\YuiTushonkaLauncher.SingleInstance";

    private readonly ILogger<SingleInstanceGuard> _logger;
    private readonly string _lockFile;

    private Mutex? _mutex;
    private FileStream? _lockStream;
    private bool _acquired;
    private bool _disposed;

    /// <summary>Creates the guard for the given lock file.</summary>
    public SingleInstanceGuard(IAppPaths paths, ILogger<SingleInstanceGuard> logger)
    {
        _logger = logger;
        _lockFile = paths.LockFile;
    }

    /// <inheritdoc />
    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_acquired)
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            _acquired = createdNew;
            if (!createdNew)
            {
                _logger.LogInformation("Another launcher instance holds the mutex {Mutex}", MutexName);
            }

            return _acquired;
        }

        try
        {
            var directory = Path.GetDirectoryName(_lockFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _lockStream = new FileStream(_lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _acquired = true;
        }
        catch (IOException exception)
        {
            _logger.LogInformation(exception, "Another launcher instance holds the lock file {LockFile}", _lockFile);
            _acquired = false;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogInformation(exception, "Cannot open the lock file {LockFile}", _lockFile);
            _acquired = false;
        }

        return _acquired;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lockStream?.Dispose();
        _lockStream = null;

        if (_mutex is not null)
        {
            if (_acquired)
            {
                _mutex.ReleaseMutex();
            }

            _mutex.Dispose();
            _mutex = null;
        }

        _acquired = false;
        _disposed = true;
    }
}
