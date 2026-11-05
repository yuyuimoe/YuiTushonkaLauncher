using System.Text.Json;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Core.Server;

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

    /// <summary>The extra Proton roots as a JSON array: one column instead of a child table for a short list.</summary>
    public string ExtraProtonRoots { get; set; } = "[]";

    /// <summary>The servers the user keeps, as a JSON array, for the same reason as the roots above.</summary>
    public string Servers { get; set; } = "[]";

    /// <summary>The name of the active server; empty means the local one.</summary>
    public string? ActiveServer { get; set; }

    public string? UmuRunPath { get; set; }

    /// <summary>Stored as text: an unknown value from a hand-edited file falls back to keeping the window open.</summary>
    public string GameStartWindow { get; set; } = "KeepOpen";

    public bool AskBeforeStoppingProcesses { get; set; } = true;

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
            ExtraProtonRoots = JsonSerializer.Serialize(settings.ExtraProtonRoots),
            Servers = JsonSerializer.Serialize(settings.Servers),
            ActiveServer = settings.ActiveServer,
            UmuRunPath = settings.UmuRunPath,
            GameStartWindow = settings.GameStartWindow.ToString(),
            AskBeforeStoppingProcesses = settings.AskBeforeStoppingProcesses,
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
            ExtraProtonRoots = ReadRoots(ExtraProtonRoots),
            Servers = ReadServers(Servers),
            ActiveServer = string.IsNullOrWhiteSpace(ActiveServer) ? null : ActiveServer,
            UmuRunPath = UmuRunPath,
            GameStartWindow = ReadGameStartWindow(GameStartWindow),
            AskBeforeStoppingProcesses = AskBeforeStoppingProcesses,
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
        ExtraProtonRoots = JsonSerializer.Serialize(settings.ExtraProtonRoots);
        Servers = JsonSerializer.Serialize(settings.Servers);
        ActiveServer = settings.ActiveServer;
        UmuRunPath = settings.UmuRunPath;
        GameStartWindow = settings.GameStartWindow.ToString();
        AskBeforeStoppingProcesses = settings.AskBeforeStoppingProcesses;
    }

    private static GameStartWindowAction ReadGameStartWindow(string? value)
    {
        return Enum.TryParse<GameStartWindowAction>(value, ignoreCase: true, out var action) ? action : GameStartWindowAction.KeepOpen;
    }

    private static ServerEntry[] ReadServers(string json)
    {
        try
        {
            var entries = JsonSerializer.Deserialize<ServerEntry[]>(json) ?? [];
            return entries.Length > 0 ? entries : [ServerEntry.Local];
        }
        catch (JsonException)
        {
            // A hand-edited row must not leave the launcher without a server to show.
            return [ServerEntry.Local];
        }
    }

    private static string[] ReadRoots(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            // A hand-edited row must not stop the launcher from starting.
            return [];
        }
    }
}
