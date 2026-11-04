using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>What the base install turned out to be.</summary>
public sealed record BaseInstallInfo
{
    /// <summary>Absolute path of the base install root.</summary>
    public required string RootPath { get; init; }

    public required InstanceLayout Layout { get; init; }

    /// <summary>Runtime directory relative to the root (<c>spt/SPT</c> or <c>spt/SPT_Runtime</c>).</summary>
    public required string RuntimeDirectory { get; init; }

    /// <summary>SPT version read from <c>SPT.Server.deps.json</c>, when parseable.</summary>
    public SemanticVersion? SptVersion { get; init; }

    /// <summary>Tarkov version read from the executable, when available.</summary>
    public string? TarkovVersion { get; init; }

    /// <summary>Non-fatal findings worth showing to the user.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Detects the layout of an SPT install and validates that it is usable as the base of an instance.
/// </summary>
public sealed partial class BaseInstallProbe(ILogger<BaseInstallProbe> logger)
{
    private static readonly string[] RequiredGameEntries = ["EscapeFromTarkov.exe", "EscapeFromTarkov_Data", "UnityPlayer.dll"];

    /// <summary>Validates the base install and reads what it can about versions.</summary>
    /// <exception cref="InstanceValidationException">The path is not a usable SPT install.</exception>
    public BaseInstallInfo Inspect(string baseInstallPath)
    {
        var issues = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(baseInstallPath))
        {
            throw new InstanceValidationException(["No base install was selected."]);
        }

        var root = Path.GetFullPath(baseInstallPath);
        if (!Directory.Exists(root))
        {
            throw new InstanceValidationException([$"The base install path does not exist: {root}"]);
        }

        var layout = DetectLayout(root);
        if (layout is null)
        {
            throw new InstanceValidationException([
                $"'{root}' does not look like an SPT install: expected 'spt/SPT_Runtime' (SPT 4.1+) or 'spt/SPT' (SPT 4.0).",
            ]);
        }

        var gameDirectory = Path.Combine(root, "gamefiles");
        if (!Directory.Exists(gameDirectory))
        {
            issues.Add($"'{gameDirectory}' is missing. Run the official SPT installer against a Tarkov installation.");
        }
        else
        {
            foreach (var entry in RequiredGameEntries)
            {
                var path = Path.Combine(gameDirectory, entry);
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    issues.Add($"'{entry}' is missing from 'gamefiles'.");
                }
            }
        }

        var runtimeDirectory = LinkPolicy.RuntimeDirectory(layout.Value);
        var runtimePath = Path.Combine(root, runtimeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(runtimePath))
        {
            issues.Add($"The SPT runtime directory '{runtimeDirectory}' is missing.");
        }

        var bepinExCore = Path.Combine(root, "spt", "BepInEx", "core");
        if (!Directory.Exists(bepinExCore) || !Directory.EnumerateFiles(bepinExCore, "*.dll").Any())
        {
            issues.Add("'spt/BepInEx/core' is missing or empty: the injector files were not installed.");
        }

        if (!File.Exists(Path.Combine(root, "spt", "winhttp.dll")))
        {
            warnings.Add("'spt/winhttp.dll' is missing: Unity Doorstop may not start on Windows.");
        }

        if (!File.Exists(Path.Combine(root, "spt", "doorstop_config.ini")))
        {
            warnings.Add("'spt/doorstop_config.ini' is missing: BepInEx will not be injected.");
        }

        if (issues.Count > 0)
        {
            throw new InstanceValidationException(issues);
        }

        var info = new BaseInstallInfo
        {
            RootPath = root,
            Layout = layout.Value,
            RuntimeDirectory = runtimeDirectory,
            SptVersion = ReadSptVersion(runtimePath, warnings),
            TarkovVersion = ReadTarkovVersion(Path.Combine(gameDirectory, "EscapeFromTarkov.exe")),
            Warnings = warnings,
        };

        logger.LogInformation(
            "Base install {Root}: layout {Layout}, SPT {SptVersion}, Tarkov {TarkovVersion}",
            info.RootPath,
            info.Layout,
            info.SptVersion?.ToString() ?? "unknown",
            info.TarkovVersion ?? "unknown"
        );

        return info;
    }

    /// <summary>Detects the layout without validating anything else.</summary>
    public static InstanceLayout? DetectLayout(string root)
    {
        if (Directory.Exists(Path.Combine(root, "spt", "SPT_Runtime")))
        {
            return InstanceLayout.Spt41Plus;
        }

        if (Directory.Exists(Path.Combine(root, "spt", "SPT")))
        {
            return InstanceLayout.Spt40;
        }

        return null;
    }

    private SemanticVersion? ReadSptVersion(string runtimePath, List<string> warnings)
    {
        var depsJson = Path.Combine(runtimePath, "SPT.Server.deps.json");
        if (!File.Exists(depsJson))
        {
            warnings.Add("'SPT.Server.deps.json' is missing: the SPT version could not be read.");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(depsJson);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            {
                warnings.Add("'SPT.Server.deps.json' has no libraries section: the SPT version could not be read.");
                return null;
            }

            foreach (var library in libraries.EnumerateObject())
            {
                // Keys look like "SPT.Server/4.1.6-RELEASE+731d7a2.20260918".
                var match = SptLibraryVersion().Match(library.Name);
                if (match.Success && SemanticVersion.TryParse(match.Groups[1].Value, out var version))
                {
                    return version;
                }
            }

            warnings.Add("No SPT version was found in 'SPT.Server.deps.json'.");
            return null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Could not read {Path}", depsJson);
            warnings.Add("'SPT.Server.deps.json' could not be read: the SPT version is unknown.");
            return null;
        }
    }

    private static string? ReadTarkovVersion(string executablePath)
    {
        // FileVersionInfo is Windows-only; the Tarkov build is read straight from the PE resource.
        return File.Exists(executablePath) ? PeVersionReader.TryReadFileVersion(executablePath) : null;
    }

    [GeneratedRegex(@"^SPT\.Server/(?<version>[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SptLibraryVersion();
}
