using YuiTushonkaLauncher.Core.Abstractions;

namespace YuiTushonkaLauncher.Infrastructure.Paths;

/// <summary>
/// Resolves the launcher's directories following the platform conventions: XDG base directories on
/// Linux, <c>%LOCALAPPDATA%</c> for data and <c>%APPDATA%</c> for configuration on Windows.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    /// <summary>Folder name used under the platform root.</summary>
    public const string AppFolderName = "YuiTushonkaLauncher";

    private readonly string[] _ownedDirectories;

    /// <summary>
    /// Creates the path set.
    /// </summary>
    /// <param name="dataRoot">Overrides the data root (used by tests).</param>
    /// <param name="configRoot">Overrides the config root (used by tests).</param>
    public AppPaths(string? dataRoot = null, string? configRoot = null)
    {
        DataRoot = Normalize(dataRoot) ?? ResolveDataRoot();
        ConfigRoot = Normalize(configRoot) ?? ResolveConfigRoot();

        LogsDirectory = Path.Join(DataRoot, "logs");
        CacheDirectory = Path.Join(DataRoot, "cache");
        DownloadsDirectory = Path.Join(DataRoot, "downloads");
        InstancesDirectory = Path.Join(DataRoot, "instances");
        DatabaseFile = Path.Join(DataRoot, "launcher.db");
        SettingsFile = Path.Join(ConfigRoot, "settings.json");
        LockFile = Path.Join(DataRoot, "launcher.lock");

        _ownedDirectories = [DataRoot, ConfigRoot, LogsDirectory, CacheDirectory, DownloadsDirectory, InstancesDirectory];
    }

    /// <inheritdoc />
    public string DataRoot { get; }

    /// <inheritdoc />
    public string ConfigRoot { get; }

    /// <inheritdoc />
    public string LogsDirectory { get; }

    /// <inheritdoc />
    public string CacheDirectory { get; }

    /// <inheritdoc />
    public string DownloadsDirectory { get; }

    /// <inheritdoc />
    public string InstancesDirectory { get; }

    /// <inheritdoc />
    public string DatabaseFile { get; }

    /// <inheritdoc />
    public string SettingsFile { get; }

    /// <inheritdoc />
    public string LockFile { get; }

    /// <inheritdoc />
    public void EnsureCreated()
    {
        foreach (var directory in _ownedDirectories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string? Normalize(string? path)
    {
        string? normalized = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        return normalized;
    }

    private static string ResolveDataRoot()
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Join(xdgDataHome, AppFolderName);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Join(localAppData, AppFolderName);
    }

    private static string ResolveConfigRoot()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Join(xdgConfigHome, AppFolderName);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Join(appData, AppFolderName);
    }
}
