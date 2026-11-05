using YuiTushonkaLauncher.Core.Packs;

namespace YuiTushonkaLauncher.Infrastructure.Packs;

/// <summary>
/// Resolves an instance-relative path from a package to an absolute one, refusing anything that would land
/// outside the instance or pass through a link on the way.
/// </summary>
public static class PackPathGuard
{
    /// <summary>Resolves <paramref name="relativePath" /> under <paramref name="instanceRoot" />.</summary>
    /// <exception cref="PackException">The path escapes the instance or crosses a symbolic link.</exception>
    public static string Resolve(string instanceRoot, string relativePath)
    {
        if (!PackRules.IsSafeRelativePath(relativePath))
        {
            throw new PackException(PackProblem.UnsafeEntry, $"The path '{relativePath}' is not a safe instance-relative path.");
        }

        var root = Path.GetFullPath(instanceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new PackException(PackProblem.UnsafeEntry, $"The path '{relativePath}' resolves outside the instance.");
        }

        RejectLinks(root, full, relativePath);

        return full;
    }

    /// <summary>Walks from the root to the target and refuses the first existing component that is a reparse point.</summary>
    private static void RejectLinks(string root, string full, string relativePath)
    {
        var current = root;

        foreach (var segment in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);

            if (!File.Exists(current) && !Directory.Exists(current))
            {
                return;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new PackException(PackProblem.UnsafeEntry, $"The path '{relativePath}' passes through a symbolic link.");
            }
        }
    }
}
