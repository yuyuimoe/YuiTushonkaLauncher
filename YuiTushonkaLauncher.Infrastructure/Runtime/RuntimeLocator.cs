using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>
/// Finds what can run a Windows program on Linux. The search rules come from the official launcher
/// (<c>compatibilitytools.d</c> folders holding a <c>compatibilitytool.vdf</c>), widened to the places
/// Steam really keeps builds: it uses <c>~/.local/share/Steam</c>, which <c>~/.steam/steam</c> may not point
/// at, and Flatpak Steam has its own tree.
/// </summary>
public sealed class RuntimeLocator : IRuntimeLocator
{
    private readonly IPlatformInfo _platform;
    private readonly IExecutableFinder _finder;
    private readonly IProcessNames _processNames;
    private readonly ILogger<RuntimeLocator> _logger;
    private readonly string _home;
    private readonly IReadOnlyList<string> _systemRoots;
    private readonly IReadOnlyList<string> _umuFallbacks;

    /// <param name="platform">Which OS rules apply.</param>
    /// <param name="finder">Resolves program names against <c>PATH</c>.</param>
    /// <param name="processNames">Running process names, to see whether the GameMode daemon is up.</param>
    /// <param name="logger">Receives what was found and what was skipped.</param>
    /// <param name="home">A home directory to search instead of the real one (tests).</param>
    /// <param name="systemRoots">System-wide build roots to search instead of the real ones (tests).</param>
    /// <param name="umuFallbacks">Where to look for <c>umu-run</c> when it is not on <c>PATH</c> (tests).</param>
    public RuntimeLocator(
        IPlatformInfo platform,
        IExecutableFinder finder,
        IProcessNames processNames,
        ILogger<RuntimeLocator> logger,
        string? home = null,
        IReadOnlyList<string>? systemRoots = null,
        IReadOnlyList<string>? umuFallbacks = null
    )
    {
        _platform = platform;
        _finder = finder;
        _processNames = processNames;
        _logger = logger;
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _systemRoots = systemRoots ?? ["/usr/share/steam/compatibilitytools.d"];
        _umuFallbacks = umuFallbacks ?? [Path.Combine(_home, ".local", "bin", "umu-run"), "/usr/bin/umu-run", "/usr/local/bin/umu-run"];
    }

    /// <inheritdoc />
    public RuntimeInventory Locate(RuntimeSearchOptions? options = null)
    {
        if (!_platform.IsLinux)
        {
            // Windows (and anything else) runs the game natively: no search, nothing to configure.
            return RuntimeInventory.Unsupported;
        }

        options ??= new RuntimeSearchOptions();
        var roots = StandardRoots().Concat(options.ExtraProtonRoots.Select(root => (root, false))).ToList();
        var builds = new List<ProtonBuild>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (root, protonOnly) in roots)
        {
            foreach (var build in ScanRoot(root, protonOnly))
            {
                var real = RealPath(build.Path);

                // One build reachable through two roots is listed once, under its real location.
                if (seen.Add(real))
                {
                    builds.Add(build with { Path = real });
                }
            }
        }

        var gamemoderun = _finder.Find("gamemoderun");
        var inventory = new RuntimeInventory
        {
            IsSupported = true,
            ProtonBuilds = builds.OrderByDescending(build => build.Name, NaturalComparer.Instance).ToList(),
            UmuRun = FindUmuRun(options),
            Wine = _finder.Find("wine"),
            Winetricks = _finder.Find("winetricks"),
            GameMode = new GameModeStatus(gamemoderun is not null, _processNames.Running().Contains("gamemoded", StringComparer.Ordinal)),
            SearchedRoots = roots.Select(root => root.Item1).Distinct(StringComparer.Ordinal).ToList(),
        };

        _logger.LogInformation(
            "Runtime inventory: {Valid} usable Proton build(s) of {Total}, umu-run {Umu}, wine {Wine}",
            builds.Count(build => build.IsValid),
            builds.Count,
            inventory.UmuRun ?? "not found",
            inventory.Wine ?? "not found"
        );

        return inventory;
    }

    /// <inheritdoc />
    public string? FindUmuRun(RuntimeSearchOptions? options = null)
    {
        if (!_platform.IsLinux)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(options?.UmuRunPath) && File.Exists(options.UmuRunPath))
        {
            return options.UmuRunPath;
        }

        return _finder.Find("umu-run") ?? _umuFallbacks.FirstOrDefault(File.Exists);
    }

    private IEnumerable<(string Root, bool ProtonOnly)> StandardRoots()
    {
        yield return (Path.Combine(_home, ".steam", "steam", "compatibilitytools.d"), false);
        yield return (Path.Combine(_home, ".local", "share", "Steam", "compatibilitytools.d"), false);
        yield return (Path.Combine(_home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "compatibilitytools.d"), false);

        foreach (var root in _systemRoots)
        {
            yield return (root, false);
        }

        // Valve's own builds live next to the games; only the "Proton …" folders there are builds.
        yield return (Path.Combine(_home, ".local", "share", "Steam", "steamapps", "common"), true);
        yield return (Path.Combine(_home, ".steam", "steam", "steamapps", "common"), true);
    }

    private IEnumerable<ProtonBuild> ScanRoot(string root, bool protonOnly)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        // A user may add a build's own folder instead of the folder that holds builds.
        if (File.Exists(Path.Combine(root, "proton")))
        {
            yield return Describe(root, Path.GetDirectoryName(root) ?? root);
            yield break;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Cannot read the Proton root {Root}: {Message}", root, exception.Message);
            yield break;
        }

        Array.Sort(directories, StringComparer.Ordinal);

        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);

            if (
                name.Contains("LegacyRuntime", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("SteamLinuxRuntime", StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (protonOnly && !name.StartsWith("Proton", StringComparison.Ordinal))
            {
                continue;
            }

            var hasScript = File.Exists(Path.Combine(directory, "proton"));
            var hasManifest = File.Exists(Path.Combine(directory, "compatibilitytool.vdf"));

            // Neither a script nor a manifest: not a Proton build at all, so it is not worth listing.
            if (hasScript || hasManifest)
            {
                yield return Describe(directory, root);
            }
        }
    }

    private static ProtonBuild Describe(string directory, string root)
    {
        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
        var problem = CheckBuild(directory);

        return new ProtonBuild
        {
            Name = name,
            Path = directory,
            Root = root,
            Version = ReadVersion(directory),
            Problem = problem,
        };
    }

    /// <summary>Why a build folder cannot be used, or <c>null</c> when it can.</summary>
    internal static string? CheckBuild(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "proton")))
        {
            return $"The 'proton' script is missing from '{directory}'.";
        }

        if (!File.Exists(Path.Combine(directory, "compatibilitytool.vdf")) && !File.Exists(Path.Combine(directory, "toolmanifest.vdf")))
        {
            return $"'{directory}' has no compatibilitytool.vdf or toolmanifest.vdf.";
        }

        if (!Directory.Exists(Path.Combine(directory, "files")) && !Directory.Exists(Path.Combine(directory, "dist")))
        {
            return $"The Wine files ('files' or 'dist') are missing from '{directory}'.";
        }

        return null;
    }

    private static string? ReadVersion(string directory)
    {
        try
        {
            var file = Path.Combine(directory, "version");

            // The file reads "<unix time> <label>", e.g. "1762000000 GE-Proton11-1".
            return File.Exists(file) ? File.ReadAllText(file).Trim().Split(' ', 2)[^1] : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The path with every symbolic link resolved, parents included. <c>~/.steam/steam</c> is normally a link to
    /// <c>~/.local/share/Steam</c>, so the same build is reachable through two roots.
    /// </summary>
    internal static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;

        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);

            try
            {
                if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    current = Path.GetFullPath(target.FullName);
                }
            }
            catch (IOException)
            {
                // Not a link we can follow: keep the name as it is.
            }
        }

        return current;
    }
}

/// <summary>Orders names the way people read them: <c>GE-Proton10-9</c> before <c>GE-Proton10-27</c>.</summary>
internal sealed class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;
        int i = 0,
            j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;

                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                var numberX = x[startX..i].TrimStart('0');
                var numberY = y[startY..j].TrimStart('0');
                var byLength = numberX.Length.CompareTo(numberY.Length);

                if (byLength != 0)
                {
                    return byLength;
                }

                var byDigits = string.CompareOrdinal(numberX, numberY);

                if (byDigits != 0)
                {
                    return byDigits;
                }

                continue;
            }

            var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));

            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
