namespace YuiTushonkaLauncher;

/// <summary>How the current process should bring up the UI.</summary>
internal enum StartupMode
{
    /// <summary>Normal start: this process owns the single-instance lock.</summary>
    Normal,

    /// <summary>Another launcher instance is already running.</summary>
    AlreadyRunning,
}
