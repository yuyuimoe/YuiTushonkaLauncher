using System.Runtime.InteropServices;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>The one libc call the launcher needs: sending a signal. .NET can kill a process but cannot ask it to stop.</summary>
#pragma warning disable SYSLIB1054 // DllImport is intentional, as in FileSystemNative: the project does not enable unsafe code.
internal static class Interop
{
    public static void Kill(int processId, int signal)
    {
        if (!OperatingSystem.IsWindows())
        {
            _ = kill(processId, signal);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);
}
