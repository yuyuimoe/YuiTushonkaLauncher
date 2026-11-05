using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Instances;

/// <summary>How an SPT folder is arranged on disk.</summary>
public enum InstallShape
{
    /// <summary>
    /// The SPT installer's output: the game under <c>gamefiles/</c> and the SPT files under <c>spt/</c>. It holds
    /// the original, untouched files and is <em>not</em> runnable — SPT and Unity Doorstop expect one root.
    /// </summary>
    Split,

    /// <summary>
    /// What SPT actually runs: the game, <c>winhttp.dll</c>, <c>Doorstop</c>, <c>BepInEx/</c> and the runtime
    /// (<c>SPT_Runtime/</c> for 4.1+, <c>SPT/</c> for 4.0) in one folder. Every instance has this shape.
    /// </summary>
    Merged,
}

/// <summary>
/// The paths inside an instance, which is always <see cref="InstallShape.Merged" />. They are the identity:
/// <c>BepInEx/plugins/…</c> and <c>SPT_Runtime/user/mods/…</c> are relative to the instance root, exactly as the
/// game and the server see them.
/// </summary>
public static class InstancePaths
{
    /// <summary>Runtime directory inside an instance (4.1+ <c>SPT_Runtime</c>, 4.0 <c>SPT</c>).</summary>
    public static string RuntimeDirectory(InstanceLayout layout)
    {
        return layout == InstanceLayout.Spt40 ? "SPT" : "SPT_Runtime";
    }

    /// <summary>The game executable, relative to the instance root.</summary>
    public const string GameExecutable = "EscapeFromTarkov.exe";

    /// <summary>The patched assembly, relative to the instance root.</summary>
    public const string PatchedAssembly = "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll";

    /// <summary>Where the instance keeps its own copy of the backup of <see cref="PatchedAssembly" />.</summary>
    public const string PatchedAssemblyBackup = "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak";

    /// <summary>The folder the SPT runtime's deltas live in.</summary>
    public static string PatchDirectory(InstanceLayout layout)
    {
        return $"{RuntimeDirectory(layout)}/SPT_Data/Launcher/Patches";
    }

    /// <summary>Where SPT writes the game's per-run logs.</summary>
    public const string GameLogs = "Logs";

    /// <summary>The server's user folder (<c>SPT_Runtime/user</c>), which belongs to one instance.</summary>
    public static string UserDirectory(InstanceLayout layout)
    {
        return $"{RuntimeDirectory(layout)}/user";
    }

    /// <summary>
    /// Directories an instance needs even when the base has nothing to put in them: per-instance state, never
    /// shared with the base or with another instance.
    /// </summary>
    public static IReadOnlyList<string> RequiredDirectories(InstanceLayout layout)
    {
        var user = UserDirectory(layout);

        return
        [
            "BepInEx/plugins",
            "BepInEx/config",
            GameLogs,
            user,
            $"{user}/certs",
            $"{user}/logs",
            $"{user}/profiles",
            $"{user}/mods",
            $"{user}/cache",
        ];
    }

    /// <summary>
    /// The instance-relative path of a base-install entry: a split base is unwrapped (<c>gamefiles/x</c> and
    /// <c>spt/x</c> both become <c>x</c>), a merged base is already in the right shape.
    /// </summary>
    public static string FromBase(string baseRelativePath, InstallShape shape)
    {
        var path = baseRelativePath.Replace('\\', '/').Trim('/');

        if (shape == InstallShape.Merged)
        {
            return path;
        }

        foreach (var wrapper in (string[])["gamefiles", "spt"])
        {
            if (path.Equals(wrapper, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (path.StartsWith(wrapper + "/", StringComparison.OrdinalIgnoreCase))
            {
                return path[(wrapper.Length + 1)..];
            }
        }

        // Anything outside the two wrapper folders (installer scaffolding) stays where it is.
        return path;
    }

    /// <summary>
    /// The merged equivalent of a path recorded under the old split layout (<c>spt/…</c> and <c>gamefiles/…</c>
    /// lose their wrapper); anything else is returned unchanged. Used to migrate stored paths and to read a
    /// <c>.sptpack</c> made before the layout changed.
    /// </summary>
    public static string LegacyToMerged(string relativePath)
    {
        return FromBase(relativePath, InstallShape.Split);
    }

    /// <summary>True when a stored path still carries a split-layout wrapper.</summary>
    public static bool IsLegacy(string relativePath)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');

        return path.StartsWith("spt/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("gamefiles/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the shape of a folder from what is on disk. A merged install has the executable next to the SPT
    /// runtime; a split one has it under <c>gamefiles/</c>. Returns <c>null</c> when it is neither.
    /// </summary>
    public static InstallShape? Detect(string root)
    {
        if (File.Exists(Path.Combine(root, GameExecutable)))
        {
            return InstallShape.Merged;
        }

        return File.Exists(Path.Combine(root, "gamefiles", GameExecutable)) ? InstallShape.Split : null;
    }
}
