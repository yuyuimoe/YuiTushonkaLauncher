using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>Looks a program up on <c>PATH</c>.</summary>
public sealed class ExecutableFinder : IExecutableFinder
{
    private readonly string? _pathVariable;

    /// <param name="pathVariable">A <c>PATH</c> value to search instead of the process's own (tests).</param>
    public ExecutableFinder(string? pathVariable = null)
    {
        _pathVariable = pathVariable;
    }

    /// <inheritdoc />
    public string? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        var path = _pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in Candidates(directory, name))
            {
                if (IsExecutable(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    internal static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Candidates(string directory, string name)
    {
        yield return Path.Combine(directory, name);

        if (OperatingSystem.IsWindows())
        {
            foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
            {
                yield return Path.Combine(directory, name + extension);
            }
        }
    }
}
