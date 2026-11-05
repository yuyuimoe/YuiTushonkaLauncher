using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>
/// Read access to the Forge API. Every call is cancellable; the implementation owns rate limiting,
/// caching and retries so callers cannot accidentally hammer the API.
/// </summary>
public interface IForgeClient
{
    /// <summary>Checks that the API is reachable.</summary>
    Task<ForgePing> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>Browses the catalogue.</summary>
    Task<ForgePage<ForgeMod>> GetModsAsync(ForgeModQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads one mod, including its description.</summary>
    Task<ForgeMod> GetModAsync(int modId, CancellationToken cancellationToken = default);

    /// <summary>Reads every published version of a mod.</summary>
    Task<IReadOnlyList<ForgeModVersion>> GetModVersionsAsync(int modId, CancellationToken cancellationToken = default);

    /// <summary>Resolves GUIDs to catalogue entries. Unmatched GUIDs are simply absent from the result.</summary>
    Task<IReadOnlyList<ForgeMod>> FindModsByGuidsAsync(IReadOnlyCollection<string> guids, CancellationToken cancellationToken = default);

    /// <summary>Checks installed packages against the latest versions for an SPT version.</summary>
    Task<ForgeUpdateReport> CheckUpdatesAsync(
        IReadOnlyCollection<InstalledPackage> installed,
        SemanticVersion sptVersion,
        CancellationToken cancellationToken = default
    );

    /// <summary>Resolves the dependency list the API reports for each installed package.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>> GetDependenciesAsync(
        IReadOnlyCollection<InstalledPackage> packages,
        SemanticVersion sptVersion,
        CancellationToken cancellationToken = default
    );

    /// <summary>Browses addons, optionally for one parent mod.</summary>
    Task<ForgePage<ForgeAddon>> GetAddonsAsync(ForgeAddonQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads every published version of an addon.</summary>
    Task<IReadOnlyList<ForgeAddonVersion>> GetAddonVersionsAsync(int addonId, CancellationToken cancellationToken = default);

    /// <summary>Lists SPT releases, newest first.</summary>
    Task<IReadOnlyList<ForgeSptVersion>> GetSptVersionsAsync(int perPage = 50, CancellationToken cancellationToken = default);

    /// <summary>Lists mod categories.</summary>
    Task<IReadOnlyList<ForgeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
}
