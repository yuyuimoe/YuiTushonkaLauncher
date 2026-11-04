namespace YuiTushonkaLauncher.Core.Abstractions;

/// <summary>
/// Every filesystem location the launcher owns. Resolved once at startup and injected, so nothing
/// else in the app builds paths from <see cref="Environment.SpecialFolder" />.
/// </summary>
public interface IAppPaths
{
    /// <summary>Root for machine-local state: database, cache, downloads, logs, instances.</summary>
    string DataRoot { get; }

    /// <summary>Root for user-editable configuration (settings file, later locale overrides).</summary>
    string ConfigRoot { get; }

    /// <summary>Directory holding the rolling application logs.</summary>
    string LogsDirectory { get; }

    /// <summary>Directory holding the content-addressed download cache.</summary>
    string CacheDirectory { get; }

    /// <summary>Directory where mod/addon archives are downloaded before installation.</summary>
    string DownloadsDirectory { get; }

    /// <summary>Default parent directory for new instances.</summary>
    string InstancesDirectory { get; }

    /// <summary>SQLite database file (used from phase 2 onwards).</summary>
    string DatabaseFile { get; }

    /// <summary>JSON settings file used until the database takes over.</summary>
    string SettingsFile { get; }

    /// <summary>Lock file backing the single-instance guard.</summary>
    string LockFile { get; }

    /// <summary>Creates every directory above, if missing. Safe to call more than once.</summary>
    void EnsureCreated();
}
