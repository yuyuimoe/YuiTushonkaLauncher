using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Reads an SPT install from disk into an <see cref="InstallFingerprint" />: shape, layout, SPT version, the game build
/// that release requires and the build that is actually there. Everything is read from the install itself, so the
/// answer is never a guess and never needs the network.
/// </summary>
public sealed partial class InstallDetector(PatchStateDetector patches, ILogger<InstallDetector> logger) : IInstallDetector
{
    /// <inheritdoc />
    public InstallFingerprint Detect(string path)
    {
        var started = Stopwatch.StartNew();
        var warnings = new List<string>();

        var root = Resolve(path);
        if (root is null)
        {
            var message = string.IsNullOrWhiteSpace(path) ? "No folder was given." : $"'{path}' does not exist.";
            logger.LogDebug("Install detection found nothing: {Reason}", message);
            return new InstallFingerprint { RootPath = path ?? string.Empty, Warnings = [message] };
        }

        var shape = InstancePaths.Detect(root);
        if (shape is null)
        {
            var message = $"'{root}' is not an SPT install: no 'EscapeFromTarkov.exe' at the root or under 'gamefiles'.";
            logger.LogDebug("Install detection found nothing: {Reason}", message);
            return new InstallFingerprint { RootPath = root, Warnings = [message] };
        }

        var layout = BaseInstallProbe.DetectLayout(root, shape.Value);
        if (layout is null)
        {
            warnings.Add(
                $"No SPT runtime folder found: expected '{RuntimeOf(shape.Value, "SPT_Runtime")}' or '{RuntimeOf(shape.Value, "SPT")}'."
            );
        }

        var runtimeDirectory = layout is null ? null : RuntimeOf(shape.Value, InstancePaths.RuntimeDirectory(layout.Value));
        var runtimePath = runtimeDirectory is null ? null : Path.Combine(root, runtimeDirectory);
        var executable = Path.Combine(
            shape.Value == InstallShape.Merged ? root : Path.Combine(root, "gamefiles"),
            InstancePaths.GameExecutable
        );
        var actualTarkov = PeVersionReader.TryReadFileVersion(executable);

        if (actualTarkov is null)
        {
            warnings.Add(
                File.Exists(executable) ? "'EscapeFromTarkov.exe' has no readable version resource." : "'EscapeFromTarkov.exe' is missing."
            );
        }

        var sptVersion = runtimePath is null ? null : ReadSptVersion(runtimePath, warnings);
        var requiredTarkov = runtimePath is null ? null : ReadRequiredTarkov(runtimePath, warnings);
        var patch = layout is null ? null : patches.Detect(root);

        var fingerprint = new InstallFingerprint
        {
            RootPath = root,
            Shape = shape,
            Layout = layout,
            RuntimeDirectory = runtimeDirectory,
            SptVersion = sptVersion,
            RequiredTarkovVersion = requiredTarkov,
            ActualTarkovVersion = actualTarkov,
            Patch = patch,
            Warnings = warnings,
        };

        logger.LogDebug("Install detection finished in {Elapsed} ms: {Summary}", started.ElapsedMilliseconds, fingerprint.Describe());

        if (!fingerprint.TarkovMatches && requiredTarkov is not null && actualTarkov is not null)
        {
            logger.LogWarning(
                "The game build does not match: SPT {Spt} requires Tarkov {Required}, found {Actual}",
                sptVersion?.ToString() ?? "unknown",
                requiredTarkov,
                actualTarkov
            );
        }

        return fingerprint;
    }

    /// <inheritdoc />
    public IReadOnlyList<InstallFingerprint> DetectMany(IEnumerable<string> roots)
    {
        var found = new List<InstallFingerprint>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            var fingerprint = Detect(root);
            if (fingerprint.IsInstall)
            {
                found.Add(fingerprint);
            }
        }

        return found;
    }

    /// <summary>Absolute path of an existing directory, or <c>null</c> when there is none.</summary>
    private static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Where a relative path lives for a given shape: under <c>spt/</c> when split, at the root when merged.</summary>
    private static string RuntimeOf(InstallShape shape, string relativePath)
    {
        return shape == InstallShape.Merged
            ? relativePath.Replace('/', Path.DirectorySeparatorChar)
            : Path.Combine("spt", relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>The SPT release this install holds, read from the server's dependency manifest.</summary>
    private static SemanticVersion? ReadSptVersion(string runtimePath, List<string> warnings)
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
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            warnings.Add("'SPT.Server.deps.json' could not be read: the SPT version is unknown.");
        }

        warnings.Add("No SPT version was found in 'SPT.Server.deps.json'.");
        return null;
    }

    /// <summary>The game build this SPT release was built against, straight from the server's own configuration.</summary>
    private static string? ReadRequiredTarkov(string runtimePath, List<string> warnings)
    {
        var core = Path.Combine(runtimePath, "SPT_Data", "configs", "core.json");
        if (!File.Exists(core))
        {
            warnings.Add("'SPT_Data/configs/core.json' is missing: the required game build is unknown.");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(core);
            using var document = JsonDocument.Parse(stream);

            if (
                document.RootElement.TryGetProperty("compatibleTarkovVersion", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } version
            )
            {
                return version;
            }

            warnings.Add("'core.json' has no 'compatibleTarkovVersion': the required game build is unknown.");
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            warnings.Add("'core.json' could not be read: the required game build is unknown.");
        }

        return null;
    }

    [GeneratedRegex(@"^SPT\.Server/(?<version>[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SptLibraryVersion();
}
