using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>Request to install an addon next to the mod it belongs to.</summary>
public sealed record AddonInstallRequest
{
    public required Guid InstanceId { get; init; }

    /// <summary>GUID of the installed parent mod.</summary>
    public required string ParentModGuid { get; init; }

    /// <summary>Forge id of the addon, for the catalogue row.</summary>
    public int? ForgeId { get; init; }

    /// <summary>Archive to install (local file).</summary>
    public string? ArchivePath { get; init; }

    /// <summary>Archive to download.</summary>
    public Uri? DownloadUrl { get; init; }

    /// <summary>Version the addon declares (Forge record), used for <c>mod_version_constraint</c> checks.</summary>
    public SemanticVersion? Version { get; init; }

    /// <summary>Mod versions this addon supports, from the Forge version record.</summary>
    public VersionRange? ModVersionConstraint { get; init; }
}

/// <summary>Result of installing or validating an addon.</summary>
public sealed record AddonInstallResult
{
    public required string Guid { get; init; }

    public required string ParentModGuid { get; init; }

    public required SemanticVersion Version { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Installs addons, which are pinned to a parent mod version rather than to an SPT version.</summary>
public interface IAddonService
{
    /// <summary>
    /// True when the addon may be installed: the parent is installed and the addon's
    /// <c>mod_version_constraint</c> admits the installed mod version.
    /// </summary>
    Task<bool> CanInstallAsync(
        Guid instanceId,
        string parentModGuid,
        VersionRange constraint,
        SemanticVersion parentVersion,
        CancellationToken cancellationToken = default
    );

    /// <summary>Installs the addon and records its files under the parent mod.</summary>
    /// <exception cref="Core.Instances.InstanceValidationException">The parent is missing or the constraint fails.</exception>
    Task<AddonInstallResult> InstallAsync(AddonInstallRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes an installed addon.</summary>
    Task UninstallAsync(Guid instanceId, string addonGuid, CancellationToken cancellationToken = default);
}
