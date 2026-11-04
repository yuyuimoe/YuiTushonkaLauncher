namespace YuiTushonkaLauncher.Core.Configuration;

/// <summary>
/// User settings. Immutable: changes are made by creating a copy with <c>with</c> and saving it.
/// </summary>
public sealed record LauncherSettings
{
    /// <summary>Where new instances are created. <c>null</c> means <see cref="Abstractions.IAppPaths.InstancesDirectory" />.</summary>
    public string? InstancesRoot { get; init; }

    /// <summary>UI language (culture name, e.g. <c>en</c> or <c>pt-BR</c>).</summary>
    public string Language { get; init; } = "en";

    /// <summary>Theme variant: <c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; init; } = "System";

    /// <summary>How many mod downloads may run at the same time.</summary>
    public int MaxConcurrentDownloads { get; init; } = 3;

    /// <summary>Days a rotated log file is kept before it is deleted.</summary>
    public int LogRetentionDays { get; init; } = 14;

    /// <summary>How many rotated log files are kept per source.</summary>
    public int LogRetentionFiles { get; init; } = 20;

    /// <summary>Set once the first-run flow has been completed.</summary>
    public bool FirstRunCompleted { get; init; }

    /// <summary>The settings used when no settings file exists yet.</summary>
    public static LauncherSettings Default
    {
        get { return new LauncherSettings(); }
    }
}
