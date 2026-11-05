using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// A Windows job object the launcher's children are put in, <b>without</b> "kill on close": closing the launcher, being
/// killed included, must leave the server and the game running ([D22](../../docs/plan/04-decisions.md#d22-the-server-and-the-game-are-detached-and-adopted)).
/// </summary>
/// <remarks>
/// The job is the record of "these processes are one run" while the launcher lives. It carries
/// <c>JOB_OBJECT_LIMIT_BREAKAWAY_OK</c> so a program that has to escape a job of its own (Steam, a launcher of a launcher)
/// still can, and it deliberately carries no limit that kills anything. The child is created with
/// <c>CREATE_BREAKAWAY_FROM_JOB</c> (<see cref="WindowsProcess" />) so a job the launcher inherited — a terminal's, an
/// installer's — does not capture it either.
/// </remarks>
[SupportedOSPlatform("windows")]
#pragma warning disable SYSLIB1054 // DllImport is intentional, as in FileSystemNative: the project does not enable unsafe code.
internal sealed class WindowsJobObject : IDisposable
{
    private const int ExtendedLimitInformation = 9;

    private const uint BreakawayOk = 0x0800;

    private readonly nint _job;

    public WindowsJobObject()
    {
        _job = CreateJobObjectW(0, null);

        if (_job == 0)
        {
            throw new InvalidOperationException($"CreateJobObject failed (error {Marshal.GetLastWin32Error()}).");
        }

        var limits = new JobObjectExtendedLimitInformation { BasicLimitInformation = { LimitFlags = BreakawayOk } };
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);

            if (!SetInformationJobObject(_job, ExtendedLimitInformation, buffer, (uint)size))
            {
                throw new InvalidOperationException($"SetInformationJobObject failed (error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Puts a started process in the job. There is a window between start and assignment, but nothing in it can kill the
    /// process: the job never kills anything.
    /// </summary>
    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_job, process.Handle))
        {
            throw new InvalidOperationException($"AssignProcessToJobObject failed (error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void Dispose()
    {
        CloseHandle(_job);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int informationClass, nint information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
