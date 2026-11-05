using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Patching;

namespace YuiTushonkaLauncher.Patching;

/// <summary>Result of reading an instance's delta set.</summary>
public sealed record DeltaSetReadResult
{
    public IReadOnlyList<DeltaDescriptor> Deltas { get; init; } = [];

    /// <summary>Deltas that could not be read, with the reason (never fatal: a bad delta is skipped).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Finds the deltas of an instance and resolves each one against the file it rewrites.
/// </summary>
/// <remarks>
/// The deltas live under <c>&lt;runtime&gt;/SPT_Data/Launcher/Patches</c> in a tree that mirrors the
/// <em>game</em> root (<c>EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.delta</c>), and the target
/// is the path with <c>.delta</c> removed, mapped onto the instance layout — so the same reader works
/// for 4.0 (<c>spt/SPT</c>) and 4.1+ (<c>spt/SPT_Runtime</c>) installs.
/// </remarks>
public sealed class DeltaSetReader(IPatchEngine engine, ILogger<DeltaSetReader> logger)
{
    /// <summary>Directory names the game and the SPT runtime are rooted at.</summary>
    private static readonly string[] GameRoots =
    [
        "EscapeFromTarkov_Data",
        "SPT_Runtime",
        "SPT",
        "BepInEx",
        "MonoBleedingEdge",
        "NLog",
        "Install_EFT",
    ];

    /// <summary>Reads every delta under <paramref name="patchesDirectory" />.</summary>
    public DeltaSetReadResult Read(string instanceRoot, string patchesDirectory)
    {
        if (!Directory.Exists(patchesDirectory))
        {
            logger.LogDebug("No patches directory at {Path}", patchesDirectory);

            return new DeltaSetReadResult();
        }

        var deltas = new List<DeltaDescriptor>();
        var warnings = new List<string>();

        foreach (
            var deltaPath in Directory.EnumerateFiles(patchesDirectory, "*" + DeltaSetLocation.DeltaExtension, SearchOption.AllDirectories)
        )
        {
            var withoutExtension = Path.GetRelativePath(patchesDirectory, deltaPath).Replace('\\', '/');
            var gameRelative = TrimToGameRoot(withoutExtension[..^DeltaSetLocation.DeltaExtension.Length]);

            DeltaHeader header;
            try
            {
                header = engine.ReadHeader(deltaPath);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                warnings.Add($"'{gameRelative}' was skipped: {exception.Message}");
                logger.LogWarning("Skipping unusable delta {Delta}: {Message}", deltaPath, exception.Message);

                continue;
            }

            var relativePath = ResolveTarget(gameRelative);

            deltas.Add(
                new DeltaDescriptor
                {
                    RelativePath = relativePath,
                    BackupRelativePath = relativePath + DeltaSetLocation.BackupSuffix,
                    DeltaPath = deltaPath,
                    ExpectedInputSize = header.ExpectedInputSize,
                    ExpectedOutputSize = header.ExpectedOutputSize,
                }
            );
        }

        logger.LogInformation("Found {Count} delta(s) in {Path}", deltas.Count, patchesDirectory);

        return new DeltaSetReadResult { Deltas = deltas, Warnings = warnings };
    }

    /// <summary>
    /// Drops the wrapper directories a delta set ships with: the real tree is
    /// <c>Patches/SPT-core/EscapeFromTarkov_Data/Managed/…</c>, and only the part from the game root on
    /// names the target.
    /// </summary>
    private static string TrimToGameRoot(string path)
    {
        var segments = path.Split('/');

        for (var index = 0; index < segments.Length; index++)
        {
            if (GameRoots.Contains(segments[index], StringComparer.OrdinalIgnoreCase))
            {
                return string.Join('/', segments[index..]);
            }
        }

        return path;
    }

    /// <summary>
    /// A delta is rooted at the game root, which is also the instance root: the path is already correct and
    /// must not be re-prefixed.
    /// </summary>
    private static string ResolveTarget(string gameRelative)
    {
        return gameRelative.Replace('\\', '/').TrimStart('/');
    }
}
