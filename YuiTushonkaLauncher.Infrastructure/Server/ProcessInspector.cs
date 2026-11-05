using System.Diagnostics;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>Looks at processes by id and stops them: SIGTERM first, SIGKILL for the whole tree after the grace period.</summary>
public sealed class ProcessInspector : IProcessInspector
{
    /// <inheritdoc />
    public ProcessIdentity? Find(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            if (process.HasExited)
            {
                return null;
            }

            string? executable = null;

            try
            {
                executable = process.MainModule?.FileName;
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Another user's process, or one that ended meanwhile: the path stays unknown and the caller treats that as "not ours".
            }

            return new ProcessIdentity(processId, new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero), executable);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? ReadLinuxExecutable(int processId)
    {
        try
        {
            return new FileInfo($"/proc/{processId}/exe").ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAlive(int processId)
    {
        return Find(processId) is not null;
    }

    /// <inheritdoc />
    public bool IsGroupAlive(int processGroupId)
    {
        if (OperatingSystem.IsWindows())
        {
            // A Windows job is identified by its handle, which the launcher no longer owns after a detach.
            return IsAlive(processGroupId);
        }

        return Interop.SignalGroup(processGroupId, 0);
    }

    /// <inheritdoc />
    public async Task<bool> StopGroupAsync(int processGroupId, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return await StopAsync(processGroupId, grace, cancellationToken);
        }

        if (!IsGroupAlive(processGroupId))
        {
            return true;
        }

        Interop.SignalGroup(processGroupId, 15);
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < grace)
        {
            if (!IsGroupAlive(processGroupId))
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        Interop.SignalGroup(processGroupId, 9);

        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!IsGroupAlive(processGroupId))
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        return !IsGroupAlive(processGroupId);
    }

    /// <inheritdoc />
    public async Task<bool> StopAsync(int processId, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        if (!IsAlive(processId))
        {
            return true;
        }

        Interop.Kill(processId, 15);

        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < grace)
        {
            if (!IsAlive(processId))
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            // Gone already, or not stoppable; the answer below says which.
        }

        return !IsAlive(processId);
    }
}
