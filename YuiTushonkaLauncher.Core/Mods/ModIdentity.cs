using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>Where an identity came from, which decides how much to trust it.</summary>
public enum ModIdentitySource
{
    /// <summary>A BepInEx <c>[BepInPlugin]</c> attribute (client mod).</summary>
    BepInPluginAttribute,

    /// <summary>An <c>IModMetadata</c> implementation read out of a server assembly.</summary>
    ServerModMetadata,

    /// <summary>Nothing usable was found.</summary>
    None,
}

/// <summary>What the launcher could read from a mod's files.</summary>
public sealed record ModIdentity
{
    /// <summary>The canonical GUID, or <c>null</c> when the reader could not find one.</summary>
    public string? Guid { get; init; }

    public string? Name { get; init; }

    public string? Author { get; init; }

    public SemanticVersion? Version { get; init; }

    public ModKind Kind { get; init; }

    public ModIdentitySource Source { get; init; }

    /// <summary>The file the identity was read from.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Reason the identity is missing, phrased for logs and the UI.</summary>
    public string? Reason { get; init; }

    /// <summary>True when a GUID was found (everything else is optional).</summary>
    public bool HasGuid
    {
        get { return !string.IsNullOrWhiteSpace(Guid); }
    }
}

/// <summary>Reads mod identity from an assembly without trusting it.</summary>
public interface IModIdentityReader
{
    /// <summary>
    /// Reads the identity of one assembly. Client mods are read from their
    /// <c>[BepInPlugin]</c> attribute; server mods from the <c>IModMetadata</c> type. Never throws:
    /// an unreadable assembly yields an identity with <see cref="ModIdentity.Source" /> = None.
    /// </summary>
    ModIdentity Read(string assemblyPath, ModKind expectedKind);
}
