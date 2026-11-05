using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Starts a child with the creation flags <see cref="ProcessStartInfo" /> does not expose, and returns the same
/// <see cref="Process" /> the managed path would. Three of those flags matter here:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>CREATE_BREAKAWAY_FROM_JOB</c>: a launcher started inside a job (a terminal, an installer, a debugger) must
/// not hand that job to the server and the game, because a job can be killed as a unit. See
/// <c>docs/plan/04-decisions.md#d22</c>.</item>
/// <item><c>CREATE_NO_WINDOW</c>: the child gets a console, but no window for it — the launcher is a window itself and
/// must not flash a black box on every server start.</item>
/// <item>nothing else. <c>KILL_ON_JOB_CLOSE</c> lives in <see cref="WindowsJobObject" />, never here.</item>
/// </list>
/// Redirected standard handles are pipes this class creates and hands to the caller as readers. A program that insists on
/// a real console handle cannot be given a pipe at all, which is why <c>ownConsole</c> exists: the child keeps the console
/// <c>CREATE_NO_WINDOW</c> makes for it and its output goes there instead of to the launcher.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsProcess
{
    private const uint CreateBreakawayFromJob = 0x0100_0000;

    private const uint CreateNoWindow = 0x0800_0000;

    private const uint CreateUnicodeEnvironment = 0x0000_0400;

    private const int StartfUseStdHandles = 0x0000_0100;

    private const uint GenericRead = 0x8000_0000;

    private const uint FileShareReadWrite = 0x0000_0003;

    private const uint OpenExisting = 3;

    private const int ErrorAccessDenied = 5;

    private const int HandleFlagInherit = 0x0000_0001;

    /// <summary>A started child, with the readers of its output when the output was redirected.</summary>
    /// <param name="Process">The child, as <see cref="Process" /> knows it.</param>
    /// <param name="Output">Standard output, when it was redirected.</param>
    /// <param name="Error">Standard error, when it was redirected.</param>
    public sealed record Started(Process Process, TextReader? Output, TextReader? Error);

    /// <summary>
    /// Starts <paramref name="command" />. With <paramref name="ownConsole" /> the child keeps the console it is given and
    /// no standard handle is redirected, so <see cref="Started.Output" /> and <see cref="Started.Error" /> are null.
    /// </summary>
    public static Started Start(LaunchCommand command, bool ownConsole)
    {
        SafeFileHandle? outputRead = null;
        SafeFileHandle? outputWrite = null;
        SafeFileHandle? errorRead = null;
        SafeFileHandle? errorWrite = null;
        SafeFileHandle? input = null;
        var started = false;

        try
        {
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };

            if (!ownConsole)
            {
                CreateInheritedPipe(out outputRead, out outputWrite);
                CreateInheritedPipe(out errorRead, out errorWrite);
                input = OpenNullDevice();

                startup.dwFlags = StartfUseStdHandles;
                startup.hStdInput = input.DangerousGetHandle();
                startup.hStdOutput = outputWrite.DangerousGetHandle();
                startup.hStdError = errorWrite.DangerousGetHandle();
            }

            var process = Create(command, startup, redirect: !ownConsole);
            started = true;

            return new Started(process, outputRead is null ? null : Reader(outputRead), errorRead is null ? null : Reader(errorRead));
        }
        finally
        {
            // The write ends belong to the child now, the NUL device to nobody: closing them is what makes the reader see
            // the end of the output when the child exits.
            outputWrite?.Dispose();
            errorWrite?.Dispose();
            input?.Dispose();

            if (!started)
            {
                outputRead?.Dispose();
                errorRead?.Dispose();
            }
        }
    }

    private static Process Create(LaunchCommand command, StartupInfo startup, bool redirect)
    {
        var commandLine = new StringBuilder(WindowsCommandLine.Build(command));
        var block = command.Environment.Count == 0 ? null : EnvironmentBlock(command.Environment);
        var pinned = default(GCHandle);

        if (block is not null)
        {
            pinned = GCHandle.Alloc(block, GCHandleType.Pinned);
        }

        try
        {
            var environment = block is null ? 0 : pinned.AddrOfPinnedObject();
            var flags = CreateNoWindow | CreateUnicodeEnvironment;

            if (!TryCreate(command, commandLine, flags | CreateBreakawayFromJob, environment, redirect, startup, out var info))
            {
                // A job the launcher did not create can forbid breakaway. The child then stays where the machine put it,
                // which is what that machine asked for; refusing to start it at all would be worse.
                if (
                    Marshal.GetLastWin32Error() != ErrorAccessDenied
                    || !TryCreate(command, commandLine, flags, environment, redirect, startup, out info)
                )
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start '{command.FileName}'.");
                }
            }

            // The thread handle is of no use to a detached child, and the process handle is closed once Process has one of
            // its own: the object outlives the handle because the child's kernel object keeps the pid reserved.
            CloseHandle(info.hThread);

            try
            {
                return Process.GetProcessById((int)info.dwProcessId);
            }
            catch (ArgumentException exception)
            {
                // It started and ended inside the microseconds this took: there is no process left to report on, and that
                // has to be an error a caller can show rather than a crash.
                throw new InvalidOperationException($"'{command.FileName}' exited before the launcher could attach to it.", exception);
            }
            finally
            {
                CloseHandle(info.hProcess);
            }
        }
        finally
        {
            if (pinned.IsAllocated)
            {
                pinned.Free();
            }
        }
    }

    private static bool TryCreate(
        LaunchCommand command,
        StringBuilder commandLine,
        uint flags,
        nint environment,
        bool redirect,
        StartupInfo startup,
        out ProcessInformation information
    )
    {
        // The pipe ends are only handed to the child through inheritance; STARTF_USESTDHANDLES alone does not copy them.
        return CreateProcessW(
            command.FileName,
            commandLine,
            0,
            0,
            inheritHandles: redirect,
            flags,
            environment,
            command.WorkingDirectory,
            ref startup,
            out information
        );
    }

    /// <summary>
    /// The launcher's environment with the command's overrides applied, as the double-null-terminated block Windows wants.
    /// </summary>
    private static char[] EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                variables[name] = value;
            }
        }

        foreach (var (name, value) in overrides)
        {
            variables[name] = value;
        }

        var builder = new StringBuilder();

        foreach (var (name, value) in variables)
        {
            builder.Append(name).Append('=').Append(value).Append('\0');
        }

        return builder.Append('\0').ToString().ToCharArray();
    }

    private static void CreateInheritedPipe(out SafeFileHandle read, out SafeFileHandle write)
    {
        // The read end must not reach the child: with the write end inherited only, the reader sees EOF when the child ends.
        if (!CreatePipeNative(out read, out write, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a pipe for the child's output.");
        }

        if (!SetHandleInformation(read, HandleFlagInherit, 0))
        {
            var error = Marshal.GetLastWin32Error();
            read.Dispose();
            write.Dispose();
            throw new Win32Exception(error, "Could not make the pipe's read end private to the launcher.");
        }
    }

    /// <summary>
    /// A read-only handle to <c>NUL</c>: the child's standard input. A program that asks "press any key" must find the end
    /// of its input instead of waiting for a keyboard nobody can reach.
    /// </summary>
    private static SafeFileHandle OpenNullDevice()
    {
        var handle = CreateFileW(@"\\.\NUL", GenericRead, FileShareReadWrite, 0, OpenExisting, 0, 0);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the NUL device for the child's input.");
        }

        return handle;
    }

    private static TextReader Reader(SafeFileHandle handle)
    {
        return new StreamReader(new FileStream(handle, FileAccess.Read, bufferSize: 8192), Encoding.UTF8);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation
    );

    [DllImport("kernel32.dll", EntryPoint = "CreatePipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipeNative(out SafeFileHandle read, out SafeFileHandle write, nint attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, int mask, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

#pragma warning disable SYSLIB1054 // DllImport is intentional, as elsewhere in this project: the structs below are private and the project does not enable unsafe code.

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;

        public string? lpReserved;

        public string? lpDesktop;

        public string? lpTitle;

        public int dwX;

        public int dwY;

        public int dwXSize;

        public int dwYSize;

        public int dwXCountChars;

        public int dwYCountChars;

        public int dwFillAttribute;

        public int dwFlags;

        public short wShowWindow;

        public short cbReserved2;

        public nint lpReserved2;

        public nint hStdInput;

        public nint hStdOutput;

        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint hProcess;

        public nint hThread;

        public uint dwProcessId;

        public uint dwThreadId;
    }
#pragma warning restore SYSLIB1054
}
