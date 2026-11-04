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

    /// <summary>Extra directories holding Proton builds (Linux), searched after the standard locations.</summary>
    public IReadOnlyList<string> ExtraProtonRoots { get; init; } = [];

    /// <summary>A <c>umu-run</c> the user picked (Linux); <c>null</c> means look on <c>PATH</c>.</summary>
    public string? UmuRunPath { get; init; }

    /// <summary>Set once the first-run flow has been completed.</summary>
    public bool FirstRunCompleted { get; init; }

    /// <summary>
    /// Value equality for the whole record. The compiler-generated one compares <see cref="ExtraProtonRoots" /> by
    /// reference, so two settings with the same folders would differ.
    /// </summary>
    public bool Equals(LauncherSettings? other)
    {
        return other is not null
            && InstancesRoot == other.InstancesRoot
            && Language == other.Language
            && Theme == other.Theme
            && MaxConcurrentDownloads == other.MaxConcurrentDownloads
            && LogRetentionDays == other.LogRetentionDays
            && LogRetentionFiles == other.LogRetentionFiles
            && FirstRunCompleted == other.FirstRunCompleted
            && UmuRunPath == other.UmuRunPath
            && ExtraProtonRoots.SequenceEqual(other.ExtraProtonRoots, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(InstancesRoot);
        hash.Add(Language);
        hash.Add(Theme);
        hash.Add(MaxConcurrentDownloads);
        hash.Add(LogRetentionDays);
        hash.Add(LogRetentionFiles);
        hash.Add(FirstRunCompleted);
        hash.Add(UmuRunPath);

        foreach (var root in ExtraProtonRoots)
        {
            hash.Add(root, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>The settings used when no settings file exists yet.</summary>
    public static LauncherSettings Default
    {
        get { return new LauncherSettings(); }
    }
}
