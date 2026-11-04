using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace YuiTushonkaLauncher.Infrastructure.FileSystem;

/// <summary>
/// The few platform calls .NET does not expose: hard links, link counts, device identity and free
/// space. Everything is best-effort — callers treat an indeterminate answer as "assume the safe case"
/// (unshared file, same volume) and let the real operation report the failure.
/// </summary>
#pragma warning disable SYSLIB1054 // DllImport is intentional: the structs are read from raw buffers.
internal static class FileSystemNative
{
    // ---- Unix ----

    private const int AtFdCwd = -100;
    private const int AtStatxSyncAsStat = 0;
    private const uint StatxNlink = 0x0000_0004;
    private const uint StatxBasicStats = 0x0000_07ff;

    /// <summary>Size of <c>struct statx</c>; the kernel writes at most this much.</summary>
    private const int StatxBufferSize = 256;

    /// <summary>Offsets inside <c>struct statx</c> (asm-generic UAPI, stable across architectures).</summary>
    private const int StatxNlinkOffset = 16;

    private const int StatxDeviceMajorOffset = 136;
    private const int StatxDeviceMinorOffset = 140;

    /// <summary>Size of <c>struct statvfs</c>.</summary>
    private const int StatvfsBufferSize = 256;

    private const int StatvfsFragmentSizeOffset = 8;
    private const int StatvfsAvailableBlocksOffset = 32;

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existingPath, string linkPath);

    [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
    private static extern int Symlink(string target, string linkPath);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int dirFd, string path, int flags, uint mask, [Out] byte[] buffer);

    [DllImport("libc", EntryPoint = "statvfs", SetLastError = true)]
    private static extern int Statvfs(string path, [Out] byte[] buffer);

    // ---- Windows ----

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// <summary>Creates a hard link; returns <c>0</c> on success, or the platform error code.</summary>
    public static int TryCreateHardLink(string existingPath, string linkPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateHardLinkW(linkPath, existingPath, IntPtr.Zero) ? 0 : Marshal.GetLastWin32Error();
        }

        return Link(existingPath, linkPath) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    /// <summary>Creates a symbolic link; returns <c>0</c> on success, or the platform error code.</summary>
    public static int TryCreateSymbolicLink(string existingPath, string linkPath)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (Directory.Exists(existingPath))
                {
                    Directory.CreateSymbolicLink(linkPath, existingPath);
                }
                else
                {
                    File.CreateSymbolicLink(linkPath, existingPath);
                }

                return 0;
            }
            catch (IOException exception)
            {
                return exception.HResult;
            }
            catch (UnauthorizedAccessException exception)
            {
                return exception.HResult;
            }
        }

        return Symlink(existingPath, linkPath) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    /// <summary>Hard-link count and device identity of a file.</summary>
    public static bool TryGetFileIdentity(string path, out int linkCount, out string? device)
    {
        linkCount = 1;
        device = null;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (!GetFileInformationByHandle(handle, out var information))
                {
                    return false;
                }

                linkCount = (int)information.NumberOfLinks;
                device = information.VolumeSerialNumber.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        var buffer = new byte[StatxBufferSize];
        if (Statx(AtFdCwd, path, AtStatxSyncAsStat, StatxBasicStats, buffer) != 0)
        {
            return false;
        }

        linkCount = (int)BitConverter.ToUInt32(buffer, StatxNlinkOffset);
        var major = BitConverter.ToUInt32(buffer, StatxDeviceMajorOffset);
        var minor = BitConverter.ToUInt32(buffer, StatxDeviceMinorOffset);
        device = $"{major}:{minor}";
        return true;
    }

    /// <summary>Bytes available to the current user on the volume holding <paramref name="path" />.</summary>
    public static long? TryGetAvailableFreeSpace(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root))
                {
                    return null;
                }

                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        var buffer = new byte[StatvfsBufferSize];
        if (Statvfs(path, buffer) != 0)
        {
            return null;
        }

        var fragmentSize = BitConverter.ToUInt64(buffer, StatvfsFragmentSizeOffset);
        var availableBlocks = BitConverter.ToUInt64(buffer, StatvfsAvailableBlocksOffset);

        return checked((long)(fragmentSize * availableBlocks));
    }
}
#pragma warning restore SYSLIB1054
