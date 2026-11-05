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

    /// <summary>Split (the installer's output) or merged (something SPT can run).</summary>
    public required InstallShape Shape { get; init; }

    /// <summary>Runtime directory relative to the root (<c>spt/SPT_Runtime</c> when split, <c>SPT_Runtime</c> when merged).</summary>
    public required string RuntimeDirectory { get; init; }

    /// <summary>True when the folder can be run as-is, which only a merged install can.</summary>
    public bool IsRunnable
    {
        get { return Shape == InstallShape.Merged; }
    }

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

        var shape = InstallShapeOf(root, issues);
        var layout = DetectLayout(root, shape);
        if (layout is null)
        {
            throw new InstanceValidationException([
                $"'{root}' does not look like an SPT install: expected '{SptPath(shape, "SPT_Runtime")}' (SPT 4.1+) or '{SptPath(shape, "SPT")}' (SPT 4.0).",
            ]);
        }

        if (shape == InstallShape.Merged)
        {
            warnings.Add(
                "This folder is already merged, so it is used as a source of files as it is: its mods, profiles and logs are not copied into the new instance."
            );
        }

        foreach (var entry in RequiredGameEntries)
        {
            var candidate = Path.Combine(root, GamePath(shape, entry));
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                issues.Add(
                    shape == InstallShape.Split
                        ? $"'{entry}' is missing from 'gamefiles'."
                        : $"'{entry}' is missing from the root of this merged install."
                );
            }
        }

        var runtimeDirectory = SptPath(shape, InstancePaths.RuntimeDirectory(layout.Value));
        var runtimePath = Path.Combine(root, runtimeDirectory);
        if (!Directory.Exists(runtimePath))
        {
            issues.Add($"The SPT runtime directory '{runtimeDirectory}' is missing.");
        }

        var bepinExCore = Path.Combine(root, SptPath(shape, "BepInEx/core"));
        if (!Directory.Exists(bepinExCore) || !Directory.EnumerateFiles(bepinExCore, "*.dll").Any())
        {
            issues.Add($"'{SptPath(shape, "BepInEx/core")}' is missing or empty: the injector files were not installed.");
        }

        if (!File.Exists(Path.Combine(root, SptPath(shape, "winhttp.dll"))))
        {
            warnings.Add($"'{SptPath(shape, "winhttp.dll")}' is missing: Unity Doorstop may not start on Windows.");
        }

        if (!File.Exists(Path.Combine(root, SptPath(shape, "doorstop_config.ini"))))
        {
            warnings.Add($"'{SptPath(shape, "doorstop_config.ini")}' is missing: BepInEx will not be injected.");
        }

        if (issues.Count > 0)
        {
            throw new InstanceValidationException(issues);
        }

        var info = new BaseInstallInfo
        {
            RootPath = root,
            Layout = layout.Value,
            Shape = shape,
            RuntimeDirectory = runtimeDirectory,
            SptVersion = ReadSptVersion(runtimePath, warnings),
            TarkovVersion = ReadTarkovVersion(Path.Combine(root, GamePath(shape, "EscapeFromTarkov.exe"))),
            Warnings = warnings,
        };

        logger.LogInformation(
            "Base install {Root}: shape {Shape}, layout {Layout}, SPT {SptVersion}, Tarkov {TarkovVersion}",
            info.RootPath,
            info.Shape,
            info.Layout,
            info.SptVersion?.ToString() ?? "unknown",
            info.TarkovVersion ?? "unknown"
        );

        return info;
    }

    /// <summary>Detects the shape of a folder, or reports why it is neither.</summary>
    private static InstallShape InstallShapeOf(string root, List<string> issues)
    {
        var shape = InstancePaths.Detect(root);

        if (shape is not null)
        {
            return shape.Value;
        }

        issues.Add(
            $"'{root}' is neither a split SPT install ('gamefiles/EscapeFromTarkov.exe' + 'spt/') nor a merged one ('EscapeFromTarkov.exe' at the root)."
        );

        // Keep inspecting so the caller sees every problem at once; the layout check below reports the rest.
        return InstallShape.Split;
    }

    /// <summary>Where the game half lives: under <c>gamefiles/</c> when split, at the root when merged.</summary>
    private static string GamePath(InstallShape shape, string relativePath)
    {
        return shape == InstallShape.Merged
            ? relativePath.Replace('/', Path.DirectorySeparatorChar)
            : Path.Combine("gamefiles", relativePath);
    }

    /// <summary>Where the SPT half lives: under <c>spt/</c> when split, at the root when merged.</summary>
    private static string SptPath(InstallShape shape, string relativePath)
    {
        return shape == InstallShape.Merged ? relativePath.Replace('/', Path.DirectorySeparatorChar) : Path.Combine("spt", relativePath);
    }

    /// <summary>Detects the layout without validating anything else.</summary>
    public static InstanceLayout? DetectLayout(string root, InstallShape shape)
    {
        var runtime = Path.Combine(root, SptPath(shape, "SPT_Runtime"));
        if (Directory.Exists(runtime))
        {
            return InstanceLayout.Spt41Plus;
        }

        var older = Path.Combine(root, SptPath(shape, "SPT"));
        if (Directory.Exists(older))
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
