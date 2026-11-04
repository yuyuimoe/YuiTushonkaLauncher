using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Decides which instance path a mod archive entry belongs to.
/// </summary>
/// <remarks>
/// An instance has SPT's merged shape, so an archive rooted at the game root (<c>BepInEx/…</c>,
/// <c>SPT_Runtime/…</c>, <c>EscapeFromTarkov_Data/…</c>) is already correct: verified against real archives,
/// where Dynamic Maps ships all three in one zip. The mapper therefore keeps the layout paths as they are and
/// only normalizes what a sloppy archive adds (a wrapper folder is handled by the installer, a leading slash
/// here). The split <c>gamefiles/</c>/<c>spt/</c> shape belongs to a base install, never to an instance.
/// </remarks>
public static class InstancePathMapper
{
    /// <summary>Top-level folders the game and SPT read, which an archive may root itself at.</summary>
    private static readonly string[] LayoutRoots = ["BepInEx/", "SPT_Runtime/", "SPT/", "EscapeFromTarkov_Data/", "Logs/", "user/"];

    /// <summary>Top-level files the game and SPT read.</summary>
    private static readonly string[] LayoutFiles =
    [
        "EscapeFromTarkov.exe",
        "UnityPlayer.dll",
        "winhttp.dll",
        "doorstop_config.ini",
        ".doorstop_version",
    ];

    /// <summary>Maps an archive-relative path to an instance-relative path.</summary>
    /// <param name="archiveRelativePath">Path inside the archive, using forward slashes.</param>
    /// <param name="layout">The instance layout, which decides the runtime directory name.</param>
    public static string Map(string archiveRelativePath, InstanceLayout layout)
    {
        var path = archiveRelativePath.Replace('\\', '/').TrimStart('/');
        var runtime = InstancePaths.RuntimeDirectory(layout);

        // The wrappers of the installer's split shape, which some archives still carry.
        path = StripPrefix(path, "gamefiles");
        path = StripPrefix(path, "spt");

        // Either runtime name means this instance's runtime.
        foreach (var other in (string[])["SPT_Runtime", "SPT"])
        {
            var rest = StripPrefix(path, other);

            if (!ReferenceEquals(rest, path))
            {
                return $"{runtime}/{rest}";
            }
        }

        // A bare "user/..." means the SPT runtime's user directory.
        var bare = StripPrefix(path, "user");
        return ReferenceEquals(bare, path) ? path : $"{InstancePaths.UserDirectory(layout)}/{bare}";
    }

    /// <summary>True when the path already belongs somewhere the game reads, so no relocation is needed.</summary>
    public static bool IsLayoutPath(string archiveRelativePath, InstanceLayout layout)
    {
        var path = archiveRelativePath.Replace('\\', '/').TrimStart('/');

        return LayoutRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            || LayoutFiles.Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Drops <paramref name="prefix" /> when the path starts with it (any case), else returns the path unchanged.</summary>
    private static string StripPrefix(string path, string prefix)
    {
        return path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ? path[(prefix.Length + 1)..] : path;
    }

    /// <summary>The default folder name inside <c>BepInEx/plugins</c> or <c>user/mods</c>.</summary>
    public static string DefaultModFolder(string guid)
    {
        var cleaned = new string([
            .. guid.ToLowerInvariant()
                .Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-'),
        ]);
        cleaned = cleaned.Trim('-', '.');

        return string.IsNullOrWhiteSpace(cleaned) ? "mod" : cleaned[..Math.Min(cleaned.Length, 64)];
    }

    /// <summary>The instance-relative root for a mod of the given kind.</summary>
    public static string ModRoot(ModKind kind, InstanceLayout layout, string folder)
    {
        return kind == ModKind.Server ? $"{InstancePaths.UserDirectory(layout)}/mods/{folder}" : $"BepInEx/plugins/{folder}";
    }
}
