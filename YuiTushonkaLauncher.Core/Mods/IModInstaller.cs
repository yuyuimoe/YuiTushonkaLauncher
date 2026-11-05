using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>Where the files of an installation come from.</summary>
public enum ModInstallSource
{
    /// <summary>Downloaded from the Forge API.</summary>
    Forge,

    /// <summary>A local archive the user picked.</summary>
    LocalArchive,

    /// <summary>Adopted from an existing installation.</summary>
    Adopted,
}

/// <summary>Request to install one mod into an instance.</summary>
public sealed record ModInstallRequest
{
    public required Guid InstanceId { get; init; }

    /// <summary>Remote archive to download, or <c>null</c> when <see cref="ArchivePath" /> is used.</summary>
    public Uri? DownloadUrl { get; init; }

    /// <summary>Local archive to install, or <c>null</c> when <see cref="DownloadUrl" /> is used.</summary>
    public string? ArchivePath { get; init; }

    /// <summary>Forge metadata, when the caller already knows it.</summary>
    public int? ForgeId { get; init; }

    /// <summary>Expected version, when the caller knows it (Forge version record).</summary>
    public SemanticVersion? ExpectedVersion { get; init; }

    /// <summary>Pin the mod so automatic updates leave it alone.</summary>
    public bool Pin { get; init; }
}

/// <summary>Outcome of an installation.</summary>
public sealed record ModInstallResult
{
    public required Guid InstalledModId { get; init; }

    public required string Guid { get; init; }

    public required string Name { get; init; }

    public required ModKind Kind { get; init; }

    public required SemanticVersion Version { get; init; }

    public required ModRecognition Recognition { get; init; }

    /// <summary>Files placed in the instance, relative to its root.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>Configuration files that were copied rather than linked.</summary>
    public IReadOnlyList<string> ConfigFiles { get; init; } = [];

    /// <summary>Non-fatal problems worth showing (unknown GUID, skipped files, …).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Installs, uninstalls and pins mods inside an instance.</summary>
public interface IModInstaller
{
    /// <summary>Installs a mod from a download or a local archive.</summary>
    /// <exception cref="Core.Instances.InstanceValidationException">The request cannot be fulfilled.</exception>
    Task<ModInstallResult> InstallAsync(ModInstallRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a mod, refusing when another installed mod depends on it.</summary>
    /// <exception cref="Core.Instances.InstanceValidationException">The mod is required by another one.</exception>
    Task UninstallAsync(Guid instanceId, string modGuid, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Pins or unpins a mod (pinned mods are never auto-updated).</summary>
    Task SetPinnedAsync(Guid instanceId, string modGuid, bool pinned, CancellationToken cancellationToken = default);
}
