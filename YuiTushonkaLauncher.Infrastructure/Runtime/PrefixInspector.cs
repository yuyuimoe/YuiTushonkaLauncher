using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>Reads what is at a Wine prefix path, and whether it could be written. Pure file-system checks.</summary>
public static class PrefixInspector
{
    private static readonly string[] RegistryFiles = ["system.reg", "user.reg"];

    private static readonly string[] WineMarkers = ["system.reg", "user.reg", "userdef.reg", "dosdevices", "drive_c"];

    /// <summary>Looks at <paramref name="path" />. Proton keeps its files in <c>pfx/</c>; umu and Wine keep them at the root.</summary>
    public static PrefixInspection Inspect(string path)
    {
        if (File.Exists(path))
        {
            return new PrefixInspection(PrefixState.Broken, path, $"'{path}' is a file, not a folder.");
        }

        if (!Directory.Exists(path))
        {
            return new PrefixInspection(PrefixState.Missing, path);
        }

        if (!Directory.EnumerateFileSystemEntries(path).Any())
        {
            return new PrefixInspection(PrefixState.Missing, path);
        }

        var root = RegistryFiles.All(file => HasContent(Path.Combine(path, "pfx", file))) ? Path.Combine(path, "pfx") : path;
        var missing = RegistryFiles.Where(file => !HasContent(Path.Combine(root, file))).ToList();

        if (missing.Count == 0)
        {
            return new PrefixInspection(PrefixState.Initialized, path);
        }

        var looksLikeWine = WineMarkers.Any(marker => Exists(Path.Combine(root, marker)));
        var reason = looksLikeWine
            ? $"The prefix is incomplete: {string.Join(" and ", missing)} {(missing.Count == 1 ? "is" : "are")} missing or empty in '{root}'."
            : $"'{path}' is not empty and does not look like a Wine prefix ({string.Join(" and ", missing)} not found).";

        return new PrefixInspection(PrefixState.Broken, path, reason, looksLikeWine);
    }

    /// <summary>
    /// The nearest folder that exists and cannot be written, when a prefix at <paramref name="path" /> could
    /// not be created or used; <c>null</c> when writing works. Proves it by creating and removing a file, as
    /// permission bits alone miss read-only mounts.
    /// </summary>
    public static string? FindUnwritableFolder(string path)
    {
        var folder = Path.GetFullPath(path);

        while (!Directory.Exists(folder))
        {
            var parent = Path.GetDirectoryName(folder);

            if (parent is null)
            {
                return folder;
            }

            folder = parent;
        }

        var probe = Path.Combine(folder, ".ytt-write-test-" + Guid.NewGuid().ToString("n"));

        try
        {
            using (File.Create(probe)) { }

            File.Delete(probe);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return folder;
        }
    }

    private static bool HasContent(string file)
    {
        return File.Exists(file) && new FileInfo(file).Length > 0;
    }

    private static bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }
}
