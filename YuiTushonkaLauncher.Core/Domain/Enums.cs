namespace YuiTushonkaLauncher.Core.Domain;

/// <summary>Directory layout of an SPT installation. 4.0 keeps the runtime under <c>spt/SPT</c>;
/// 4.1 and later under <c>spt/SPT_Runtime</c>.</summary>
public enum InstanceLayout
{
    /// <summary>SPT 4.0: <c>spt/SPT/...</c>.</summary>
    Spt40,

    /// <summary>SPT 4.1+: <c>spt/SPT_Runtime/...</c>.</summary>
    Spt41Plus,
}

/// <summary>Where a mod lives: BepInEx plugins (client) or the SPT server runtime (server).</summary>
public enum ModKind
{
    /// <summary>BepInEx plugin, loaded by the game process.</summary>
    Client,

    /// <summary>C# assembly implementing <c>IModMetadata</c>, loaded by the SPT server.</summary>
    Server,

    /// <summary>The package ships both halves.</summary>
    Both,
}

/// <summary>How confidently a locally installed mod was matched to the Forge catalogue.</summary>
public enum ModRecognition
{
    /// <summary>GUID resolved and found in the Forge API.</summary>
    Recognized,

    /// <summary>GUID resolved but the Forge API does not know it.</summary>
    Unrecognized,

    /// <summary>No GUID could be read from the installed files.</summary>
    GuidMissing,
}

/// <summary>Relationship between a package and one of its dependencies.</summary>
public enum DependencyKind
{
    /// <summary>Must be installed, within the version range.</summary>
    Required,

    /// <summary>Enhances the package but is not needed to load it.</summary>
    Optional,

    /// <summary>Refuses to coexist with the package.</summary>
    Incompatible,
}

/// <summary>How the game process is executed on this machine.</summary>
public enum RuntimeKind
{
    /// <summary>Runs natively (Windows).</summary>
    Native,

    /// <summary>Runs through a Steam Proton build.</summary>
    Proton,

    /// <summary>Runs through a plain Wine binary.</summary>
    Wine,
}

/// <summary>Which side of a package a dependency or entry refers to.</summary>
public enum PackageKind
{
    /// <summary>A mod.</summary>
    Mod,

    /// <summary>An addon attached to a mod.</summary>
    Addon,
}
