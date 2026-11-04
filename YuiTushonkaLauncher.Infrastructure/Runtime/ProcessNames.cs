using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>The names of the running processes, read from <c>/proc</c> on Linux and from the OS elsewhere.</summary>
public sealed class ProcessNames : IProcessNames
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> Running()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (OperatingSystem.IsLinux() && Directory.Exists("/proc"))
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out _))
                {
                    continue;
                }

                try
                {
                    names.Add(File.ReadAllText(Path.Combine(directory, "comm")).Trim());
                }
                catch (IOException)
                {
                    // The process ended while we were looking.
                }
                catch (UnauthorizedAccessException)
                {
                    // Not ours to read; the daemon we look for runs as the user.
                }
            }

            return names;
        }

        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                names.Add(process.ProcessName);
            }
        }

        return names;
    }
}
