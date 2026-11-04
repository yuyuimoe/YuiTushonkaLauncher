namespace YuiTushonkaLauncher.Core.Domain;

/// <summary>An SPT installation managed by the launcher: one game copy plus its own mods and profiles.</summary>
public sealed class Instance
{
    public Guid Id { get; set; }

    /// <summary>Display name; also the folder name, restricted to <c>[A-Za-z0-9_-]+</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path of the instance root.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Absolute path of the user-supplied install this instance links from.</summary>
    public string BaseGamePath { get; set; } = string.Empty;

    /// <summary>SPT version the instance was created for.</summary>
    public SemanticVersion SptVersion { get; set; }

    /// <summary>Tarkov build reported by the base install.</summary>
    public string TarkovVersion { get; set; } = string.Empty;

    public InstanceLayout Layout { get; set; }

    /// <summary>Execution profile (native, Proton, Wine); <c>null</c> means the default profile.</summary>
    public Guid? RuntimeProfileId { get; set; }

    /// <summary>Port the local SPT server listens on for this instance.</summary>
    public int ServerPort { get; set; } = 6969;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLaunchedAt { get; set; }

    /// <summary>Last known patch state, refreshed whenever the instance is inspected.</summary>
    public PatchInfo Patch { get; set; } = new();

    public List<InstalledMod> InstalledMods { get; set; } = [];

    public ServerProfile? ServerProfile { get; set; }
}

/// <summary>Patch state of an instance's game binaries.</summary>
public sealed class PatchInfo
{
    public bool IsPatched { get; set; }

    /// <summary>SPT version the applied deltas belong to.</summary>
    public string? PatchSetVersion { get; set; }

    /// <summary>SHA-256 of the backed-up (original) file, when a backup exists.</summary>
    public string? OriginalSha256 { get; set; }

    /// <summary>SHA-256 of the patched file.</summary>
    public string? PatchedSha256 { get; set; }

    /// <summary>Absolute path of the <c>.spt-bak</c> file.</summary>
    public string? BackupPath { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }
}

/// <summary>A released SPT version, cached from the Forge API.</summary>
public sealed class SptRelease
{
    public Guid Id { get; set; }

    /// <summary>Forge-side identifier.</summary>
    public int ForgeId { get; set; }

    public SemanticVersion Version { get; set; }

    /// <summary>Tarkov build this SPT release targets, when known.</summary>
    public string? TarkovVersion { get; set; }

    /// <summary>Number of mods published for this release, as reported by the API.</summary>
    public int ModCount { get; set; }

    /// <summary>Release page URL.</summary>
    public string? ReleaseUrl { get; set; }

    /// <summary>True for the newest release the API knows about.</summary>
    public bool IsCurrent { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>A mod in the Forge catalogue, identified by its GUID.</summary>
public sealed class Mod
{
    public Guid Id { get; set; }

    /// <summary>Forge-side identifier; <c>null</c> while unknown.</summary>
    public int? ForgeId { get; set; }

    /// <summary>Canonical identity: BepInEx plugin GUID or <c>IModMetadata.ModGuid</c>.</summary>
    public string Guid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public ModKind Kind { get; set; }

    public string? ThumbnailUrl { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<ModVersion> Versions { get; set; } = [];
}

/// <summary>One published version of a <see cref="Mod" />.</summary>
public sealed class ModVersion
{
    public Guid Id { get; set; }

    public Guid ModId { get; set; }

    public Mod? Mod { get; set; }

    public SemanticVersion Version { get; set; }

    /// <summary>SPT versions this release supports.</summary>
    public VersionRange SptConstraint { get; set; } = VersionRange.Any;

    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Size reported by the API; advisory, the redirect target may disagree.</summary>
    public long? ContentLength { get; set; }

    /// <summary>SHA-256 computed locally after the first download.</summary>
    public string? Sha256 { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>An addon (translation, extra content) attached to a mod.</summary>
public sealed class Addon
{
    public Guid Id { get; set; }

    public int? ForgeId { get; set; }

    /// <summary>Forge identifier of the parent mod.</summary>
    public int ParentForgeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    /// <summary>True when the addon was detached from its parent on Forge.</summary>
    public bool IsDetached { get; set; }

    public string? ThumbnailUrl { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<AddonVersion> Versions { get; set; } = [];
}

/// <summary>One published version of an <see cref="Addon" />.</summary>
public sealed class AddonVersion
{
    public Guid Id { get; set; }

    public Guid AddonId { get; set; }

    public Addon? Addon { get; set; }

    public SemanticVersion Version { get; set; }

    /// <summary>
    /// Mod versions this addon supports. Addons are pinned to a <em>mod</em> version range, not to an
    /// SPT version — the API exposes <c>mod_version_constraint</c> here.
    /// </summary>
    public VersionRange ModVersionConstraint { get; set; } = VersionRange.Any;

    public string DownloadUrl { get; set; } = string.Empty;

    public long? ContentLength { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>A dependency or incompatibility declared by a package.</summary>
public sealed class Dependency
{
    public Guid Id { get; set; }

    /// <summary>GUID of the package that declares the relationship.</summary>
    public string OwnerGuid { get; set; } = string.Empty;

    public PackageKind OwnerKind { get; set; }

    /// <summary>GUID of the package being depended on (or refused).</summary>
    public string DependencyGuid { get; set; } = string.Empty;

    public DependencyKind Kind { get; set; }

    /// <summary>Accepted versions of the dependency; <see cref="VersionRange.Any" /> for incompatibilities.</summary>
    public VersionRange VersionRange { get; set; } = VersionRange.Any;
}
