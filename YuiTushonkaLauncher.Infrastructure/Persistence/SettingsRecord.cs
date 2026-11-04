using YuiTushonkaLauncher.Core.Configuration;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>
/// The single settings row. Kept internal: consumers see <see cref="LauncherSettings" /> through
/// <see cref="Core.Abstractions.ISettingsService" />, never the storage shape.
/// </summary>
internal sealed class SettingsRecord
{
    /// <summary>There is exactly one settings row, always with this id.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string? InstancesRoot { get; set; }

    public string Language { get; set; } = "en";

    public string Theme { get; set; } = "System";

    public int MaxConcurrentDownloads { get; set; } = 3;

    public int LogRetentionDays { get; set; } = 14;

    public int LogRetentionFiles { get; set; } = 20;

    public bool FirstRunCompleted { get; set; }

    public static SettingsRecord FromSettings(LauncherSettings settings)
    {
        return new SettingsRecord
        {
            InstancesRoot = settings.InstancesRoot,
            Language = settings.Language,
            Theme = settings.Theme,
            MaxConcurrentDownloads = settings.MaxConcurrentDownloads,
            LogRetentionDays = settings.LogRetentionDays,
            LogRetentionFiles = settings.LogRetentionFiles,
            FirstRunCompleted = settings.FirstRunCompleted,
        };
    }

    public LauncherSettings ToSettings()
    {
        return new LauncherSettings
        {
            InstancesRoot = InstancesRoot,
            Language = Language,
            Theme = Theme,
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            LogRetentionDays = LogRetentionDays,
            LogRetentionFiles = LogRetentionFiles,
            FirstRunCompleted = FirstRunCompleted,
        };
    }

    public void CopyFromSettings(LauncherSettings settings)
    {
        InstancesRoot = settings.InstancesRoot;
        Language = settings.Language;
        Theme = settings.Theme;
        MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
        LogRetentionDays = settings.LogRetentionDays;
        LogRetentionFiles = settings.LogRetentionFiles;
        FirstRunCompleted = settings.FirstRunCompleted;
    }
}
