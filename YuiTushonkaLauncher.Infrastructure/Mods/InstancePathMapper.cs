using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Maps paths from a mod archive onto the instance layout.
/// </summary>
/// <remarks>
/// Mod archives are rooted at the <em>game</em> root — <c>BepInEx/…</c>, <c>SPT_Runtime/…</c>,
/// <c>EscapeFromTarkov_Data/…</c> — while an instance keeps the game under <c>gamefiles/</c> and the
/// SPT runtime under <c>spt/</c> (the split the official installer and the user's own
/// <c>create-spt-instance.sh</c> produce). Verified against real archives: Dynamic Maps ships
/// <c>BepInEx/plugins</c>, <c>SPT_Runtime/user/mods</c> and <c>EscapeFromTarkov_Data/Managed</c> in one
/// zip, and writing those verbatim would scatter files outside the trees the game reads.
/// </remarks>
public static class InstancePathMapper
{
    /// <summary>Prefixes that belong to the game half of the instance.</summary>
    private static readonly string[] GameRoots = ["EscapeFromTarkov_Data/", "MonoBleedingEdge/", "NLog/", "Install_EFT/"];

    /// <summary>Files that live at the game root but are not under a directory.</summary>
    private static readonly string[] GameRootFiles = ["EscapeFromTarkov.exe", "UnityPlayer.dll", "Register Game.exe"];

    /// <summary>Prefixes that belong to the SPT half of the instance.</summary>
    private static readonly string[] SptRoots = ["BepInEx/"];

    /// <summary>Files that live at the SPT root but are not under a directory.</summary>
    private static readonly string[] SptRootFiles = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version"];

    /// <summary>
    /// Maps an archive-relative path to an instance-relative path.
    /// </summary>
    /// <param name="archiveRelativePath">Path inside the archive, using forward slashes.</param>
    /// <param name="layout">The instance layout, which decides the runtime directory name.</param>
    public static string Map(string archiveRelativePath, InstanceLayout layout)
    {
        var path = archiveRelativePath.Replace('\\', '/').TrimStart('/');

        // Already instance-relative ("spt/..." checked after the archive roots, because "SPT/..."
        // would otherwise match case-insensitively and stay uppercase).
        if (path.StartsWith("gamefiles/", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        foreach (var root in GameRoots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return "gamefiles/" + path;
            }
        }

        if (GameRootFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return "gamefiles/" + path;
        }

        if (path.StartsWith("SPT_Runtime/", StringComparison.Ordinal))
        {
            var rest = path["SPT_Runtime/".Length..];
            return layout == InstanceLayout.Spt40 ? $"spt/SPT/{rest}" : $"spt/SPT_Runtime/{rest}";
        }

        if (path.StartsWith("SPT/", StringComparison.Ordinal))
        {
            return "spt/" + path;
        }

        if (path.StartsWith("spt/", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        foreach (var root in SptRoots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return "spt/" + path;
            }
        }

        if (SptRootFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return "spt/" + path;
        }

        if (path.StartsWith("user/", StringComparison.OrdinalIgnoreCase))
        {
            // Bare "user/..." means the SPT runtime's user directory.
            var runtime = layout == InstanceLayout.Spt40 ? "spt/SPT" : "spt/SPT_Runtime";
            return $"{runtime}/{path}";
        }

        // Anything else (a README, a bare assembly) is relocated by the caller.
        return path;
    }

    /// <summary>True when the path already belongs somewhere the game reads, so no relocation is needed.</summary>
    public static bool IsLayoutPath(string archiveRelativePath, InstanceLayout layout)
    {
        var mapped = Map(archiveRelativePath, layout);
        return mapped.StartsWith("gamefiles/", StringComparison.OrdinalIgnoreCase)
            || mapped.StartsWith("spt/", StringComparison.OrdinalIgnoreCase);
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
        return kind == ModKind.Server ? $"{RuntimeDirectory(layout)}/user/mods/{folder}" : $"spt/BepInEx/plugins/{folder}";
    }

    private static string RuntimeDirectory(InstanceLayout layout)
    {
        return layout == InstanceLayout.Spt40 ? "spt/SPT" : "spt/SPT_Runtime";
    }
}
