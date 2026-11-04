using YuiTushonkaLauncher.Core.Configuration;

namespace YuiTushonkaLauncher.Core.Abstractions;

/// <summary>
/// Persists <see cref="LauncherSettings" />. Backed by a JSON file in phase 1 and by the SQLite
/// database from phase 2 onwards; consumers only see this contract.
/// </summary>
public interface ISettingsService
{
    /// <summary>The settings currently in memory. Never <c>null</c>.</summary>
    LauncherSettings Current { get; }

    /// <summary>Raised after <see cref="Current" /> changes, with the new value.</summary>
    event EventHandler<LauncherSettings>? Changed;

    /// <summary>Reads the settings from storage, falling back to defaults when absent or corrupt.</summary>
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the settings atomically and updates <see cref="Current" />.</summary>
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
}
