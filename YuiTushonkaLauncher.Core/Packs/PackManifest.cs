using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Packs;

/// <summary>Format constants of a <c>.sptpack</c>.</summary>
public static class PackFormat
{
    /// <summary>The only format version this build reads and writes.</summary>
    public const int CurrentVersion = 1;

    public const string ManifestEntry = "manifest.json";

    public const string ReadmeEntry = "README.txt";

    /// <summary>Directory inside the zip that holds the shared config files.</summary>
    public const string ConfigsDirectory = "configs/";

    public const string FileExtension = ".sptpack";
}

/// <summary>
/// The composition of an instance, as written to <c>manifest.json</c>. Exact versions, never ranges: a
/// package must be importable on a machine that has never seen the source instance.
/// </summary>
public sealed record PackManifest
{
    public required int FormatVersion { get; init; }

    /// <summary>When the package was written. Not part of the share code.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Launcher version that wrote it. Not part of the share code.</summary>
    public required string AppVersion { get; init; }

    /// <summary>Name of the source instance, for display only. Not part of the share code.</summary>
    public required string InstanceName { get; init; }

    public required string SptVersion { get; init; }

    public required string TarkovVersion { get; init; }

    public required InstanceLayout Layout { get; init; }

    public IReadOnlyList<PackMod> Mods { get; init; } = [];

    public IReadOnlyList<PackAddon> Addons { get; init; } = [];

    public IReadOnlyList<PackConfig> Configs { get; init; } = [];

    /// <summary>
    /// SHA-256 of the canonical composition (everything except the four "not part of the share code"
    /// fields). Two exports of an unchanged instance have the same code. It detects corruption and
    /// accidental edits; it is <b>not</b> a signature and proves nothing about who made the package.
    /// </summary>
    public string ShareCode { get; init; } = string.Empty;
}

/// <summary>One installed mod. The package names it; it never carries a download URL or a binary.</summary>
public sealed record PackMod
{
    public required string Guid { get; init; }

    /// <summary>Forge id, or <c>null</c> when the mod did not come from the Forge (cannot be fetched).</summary>
    public int? ForgeId { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public required ModKind Kind { get; init; }

    public bool Pinned { get; init; }

    /// <summary>
    /// Digest of every file the mod shipped (<c>path:sha256</c> lines, sorted, hashes as shipped). The import recomputes it
    /// after installing, so a third-party host that serves different content than the exporter had is
    /// noticed instead of silently trusted.
    /// </summary>
    public string ContentSha256 { get; init; } = string.Empty;
}

/// <summary>An addon attached to a mod.</summary>
public sealed record PackAddon
{
    public required string ParentGuid { get; init; }

    public required string Guid { get; init; }

    public int? ForgeId { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public string ContentSha256 { get; init; } = string.Empty;
}

/// <summary>A configuration file the user edited.</summary>
public sealed record PackConfig
{
    /// <summary>GUID of the mod (or addon) that owns the file. An import only writes files it owns.</summary>
    public required string ModGuid { get; init; }

    /// <summary>Instance-relative path, forward slashes.</summary>
    public required string Path { get; init; }

    /// <summary>Hash of the shared content.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Hash of the file as the mod shipped it.</summary>
    public string? OriginalSha256 { get; init; }
}
