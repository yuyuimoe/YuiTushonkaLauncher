namespace YuiTushonkaLauncher.Core.Packs;

/// <summary>Why a package was refused.</summary>
public enum PackProblem
{
    /// <summary>Not a zip, no manifest, or JSON that does not match the schema.</summary>
    Malformed,

    /// <summary>The manifest is written in a newer format than this build understands.</summary>
    FormatTooNew,

    /// <summary>An entry name or config path could escape the instance (zip-slip, absolute path, …).</summary>
    UnsafeEntry,

    /// <summary>The package contains an executable or binary file.</summary>
    ForbiddenFile,

    /// <summary>A size, count or ratio cap was exceeded.</summary>
    TooLarge,

    /// <summary>Content does not match the hash the manifest declares, or the share code is wrong.</summary>
    Tampered,
}

/// <summary>A package that must not be imported. The message is meant to be shown to the user.</summary>
public sealed class PackException(PackProblem problem, string message) : Exception(message)
{
    public PackProblem Problem { get; } = problem;
}

/// <summary>
/// The pure rules that decide what a package may contain. Everything a package says is untrusted, so
/// every rule fails closed.
/// </summary>
public static class PackRules
{
    /// <summary>Largest manifest accepted.</summary>
    public const int MaxManifestBytes = 1024 * 1024;

    /// <summary>Largest single config file accepted.</summary>
    public const long MaxConfigBytes = 5L * 1024 * 1024;

    /// <summary>Largest total of uncompressed bytes accepted.</summary>
    public const long MaxTotalBytes = 64L * 1024 * 1024;

    /// <summary>Most entries a package may hold.</summary>
    public const int MaxEntries = 2000;

    public const int MaxMods = 500;

    public const int MaxPathLength = 400;

    /// <summary>Extensions a config may have. Anything else is not a config.</summary>
    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json",
        ".ini",
        ".toml",
        ".yaml",
        ".yml",
        ".txt",
        ".cfg",
    };

    /// <summary>
    /// Extensions that are refused outright, even where the allowlist already says no: a second line of
    /// defence that gives a specific error for the case that matters (a binary in a package).
    /// </summary>
    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll",
        ".exe",
        ".bundle",
        ".pak",
        ".dat",
        ".so",
        ".dylib",
        ".bat",
        ".cmd",
        ".sh",
        ".ps1",
        ".msi",
        ".jar",
        ".com",
        ".scr",
        ".lnk",
    };

    /// <summary>True when a config with this name may be shared.</summary>
    public static bool IsAllowedConfigExtension(string path)
    {
        return ConfigExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>True when a file with this name must never be in a package.</summary>
    public static bool IsForbiddenExtension(string path)
    {
        return ForbiddenExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// True when <paramref name="path" /> is a relative path that stays inside its root: forward slashes
    /// only, no <c>..</c>, no drive or UNC prefix, no empty/dot segments, nothing Windows would reinterpret.
    /// </summary>
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength)
        {
            return false;
        }

        if (path.Contains('\\') || path.Contains('\0') || path.StartsWith('/') || path.EndsWith('/'))
        {
            return false;
        }

        // "C:foo", "C:/foo", "\\server\share" and the like.
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (!IsSafeSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds the zip entry name that holds a config.</summary>
    public static string ConfigEntryName(string instanceRelativePath)
    {
        return PackFormat.ConfigsDirectory + instanceRelativePath;
    }

    private static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or "..")
        {
            return false;
        }

        // Windows silently strips trailing dots and spaces, so "evil.json." names "evil.json".
        if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.StartsWith(' '))
        {
            return false;
        }

        foreach (var character in segment)
        {
            if (char.IsControl(character) || character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            {
                return false;
            }
        }

        // Windows device names are reserved with or without an extension.
        var stem = segment.Contains('.') ? segment[..segment.IndexOf('.')] : segment;

        return !IsReservedDeviceName(stem);
    }

    private static bool IsReservedDeviceName(string stem)
    {
        if (
            stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && char.IsAsciiDigit(stem[3]);
    }
}
