namespace YuiTushonkaLauncher.Core.Domain;

/// <summary>A mod installed inside one instance. The same mod can be installed in several instances
/// at different versions, so this is separate from the catalogue <see cref="Mod" />.</summary>
public sealed class InstalledMod
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }

    public Instance? Instance { get; set; }

    /// <summary>Catalogue entry, when the mod was recognised.</summary>
    public Guid? ModId { get; set; }

    public Mod? Mod { get; set; }

    /// <summary>GUID read from the installed files (plugin attribute or <c>IModMetadata</c>).</summary>
    public string Guid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ModKind Kind { get; set; }

    public SemanticVersion Version { get; set; }

    /// <summary>Blocks automatic updates when true.</summary>
    public bool IsPinned { get; set; }

    /// <summary>GUID of the mod this row is an addon of, or <c>null</c> for a mod.</summary>
    public string? ParentModGuid { get; set; }

    /// <summary>True when this row is an addon attached to <see cref="ParentModGuid" />.</summary>
    public bool IsAddon
    {
        get { return ParentModGuid is not null; }
    }

    public ModRecognition Recognition { get; set; }

    /// <summary>Where the mod came from: a Forge download, a local archive, or an adopted install.</summary>
    public InstalledModSource Source { get; set; }

    public DateTimeOffset InstalledAt { get; set; }

    public List<InstalledFile> Files { get; set; } = [];
}

/// <summary>Where an installed mod came from.</summary>
public enum InstalledModSource
{
    /// <summary>Downloaded from the Forge API.</summary>
    Forge,

    /// <summary>Installed from a local archive.</summary>
    LocalArchive,

    /// <summary>Adopted from a pre-existing installation.</summary>
    Adopted,
}

/// <summary>One file placed by an installed mod, with the hash recorded at install time.</summary>
public sealed class InstalledFile
{
    public Guid Id { get; set; }

    public Guid InstalledModId { get; set; }

    public InstalledMod? InstalledMod { get; set; }

    /// <summary>Path relative to the instance root; never absolute, never contains <c>..</c>.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Hash of the file as installed.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long Length { get; set; }

    /// <summary>True for user-editable configuration files (candidates for export).</summary>
    public bool IsConfig { get; set; }

    /// <summary>Hash of the file the mod shipped, used to detect local edits of configs.</summary>
    public string? OriginalSha256 { get; set; }
}

/// <summary>A shareable manifest: the composition of an instance, without binaries.</summary>
public sealed class ModList
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public SemanticVersion SptVersion { get; set; }

    public string TarkovVersion { get; set; } = string.Empty;

    /// <summary>Share code: SHA-256 over the canonical manifest.</summary>
    public string? ManifestSha256 { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ModListEntry> Entries { get; set; } = [];
}

/// <summary>One exact package version inside a <see cref="ModList" />.</summary>
public sealed class ModListEntry
{
    public Guid Id { get; set; }

    public Guid ModListId { get; set; }

    public ModList? ModList { get; set; }

    public string Guid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public SemanticVersion Version { get; set; }

    public PackageKind Kind { get; set; }

    public bool IsPinned { get; set; }
}

/// <summary>How a game process is executed: natively, through Proton or through Wine.</summary>
public sealed class RuntimeProfile
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public RuntimeKind Kind { get; set; }

    /// <summary>Executable used to launch (umu, Proton's <c>proton</c>, or a <c>wine</c> binary).</summary>
    public string? Executable { get; set; }

    /// <summary>Wine prefix path on Linux.</summary>
    public string? PrefixPath { get; set; }

    /// <summary>Extra environment variables applied to the game and server processes.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>Additional command-line arguments.</summary>
    public string? ExtraArguments { get; set; }

    public bool IsDefault { get; set; }
}

/// <summary>Local SPT server configuration and cached state for one instance.</summary>
public sealed class ServerProfile
{
    public Guid Id { get; set; }

    public Guid InstanceId { get; set; }

    public Instance? Instance { get; set; }

    public int Port { get; set; } = 6969;

    /// <summary>Base URL of the local server, e.g. <c>https://127.0.0.1:6969</c>.</summary>
    public string BackendUrl { get; set; } = string.Empty;

    /// <summary>Automatically install mods the connected server requires.</summary>
    public bool AutoSyncMods { get; set; }

    /// <summary>Profile id last used with this instance.</summary>
    public string? LastProfileId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
