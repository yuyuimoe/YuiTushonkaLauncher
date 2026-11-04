using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Updates;
using YuiTushonkaLauncher.Services;
using YuiTushonkaLauncher.Startup;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// Settings: language, theme, folders, the two retention numbers and the two "what happens when" settings. Saving
/// applies the language and the theme immediately, because both are visible in this window.
/// </summary>
public partial class SettingsPageViewModel : PageViewModel
{
    private readonly ISettingsService _settings;
    private readonly IThemeController _theme;
    private readonly IAppPaths _paths;
    private readonly IUpdateChecker? _updates;
    private readonly IUrlOpener? _links;

    public SettingsPageViewModel(
        ILocalizer localizer,
        ISettingsService settings,
        IThemeController theme,
        IAppPaths paths,
        IUpdateChecker? updates = null,
        IUrlOpener? links = null
    )
        : base(localizer)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;
        _updates = updates;
        _links = links;

        Languages = [.. localizer.AvailableLanguages.Select(language => new LanguageOption(language.Code, language.NativeName))];
        Themes =
        [
            new("System", Text("settings.themeSystem")),
            new("Light", Text("settings.themeLight")),
            new("Dark", Text("settings.themeDark")),
        ];
        Windows =
        [
            new(GameStartWindowAction.KeepOpen, Text("settings.windowKeepOpen")),
            new(GameStartWindowAction.Minimise, Text("settings.windowMinimise")),
            new(GameStartWindowAction.Close, Text("settings.windowClose")),
        ];

        Apply(_settings.Current);
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public IReadOnlyList<WindowOption> Windows { get; }

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial ThemeOption? SelectedTheme { get; set; }

    [ObservableProperty]
    public partial WindowOption? SelectedWindow { get; set; }

    [ObservableProperty]
    public partial string InstancesRoot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int MaxConcurrentDownloads { get; set; }

    [ObservableProperty]
    public partial int LogRetentionDays { get; set; }

    [ObservableProperty]
    public partial int LogRetentionFiles { get; set; }

    [ObservableProperty]
    public partial bool AskBeforeStoppingProcesses { get; set; }

    public string Title
    {
        get { return Text("settings.title"); }
    }

    public string LanguageLabel
    {
        get { return Text("settings.language"); }
    }

    public string ThemeLabel
    {
        get { return Text("settings.theme"); }
    }

    public string InstancesRootLabel
    {
        get { return Text("settings.instancesRoot"); }
    }

    public string DownloadsLabel
    {
        get { return Text("settings.downloads"); }
    }

    public string RetentionDaysLabel
    {
        get { return Text("settings.retentionDays"); }
    }

    public string RetentionFilesLabel
    {
        get { return Text("settings.retentionFiles"); }
    }

    public string GameStartWindowLabel
    {
        get { return Text("settings.gameStartWindow"); }
    }

    public string AskBeforeStopLabel
    {
        get { return Text("settings.askBeforeStop"); }
    }

    public string SaveLabel
    {
        get { return Text("common.save"); }
    }

    public string PathsLabel
    {
        get { return Text("settings.paths"); }
    }

    public string DataRootLabel
    {
        get { return Text("settings.dataRoot"); }
    }

    public string ConfigRootLabel
    {
        get { return Text("settings.configRoot"); }
    }

    public string LogsDirectoryLabel
    {
        get { return Text("settings.logsDirectory"); }
    }

    public string InstancesDirectoryLabel
    {
        get { return Text("settings.instancesDirectory"); }
    }

    public string AboutLabel
    {
        get { return Text("settings.about"); }
    }

    public string VersionLabel
    {
        get { return Text("settings.version"); }
    }

    public string DataRoot
    {
        get { return _paths.DataRoot; }
    }

    public string ConfigRoot
    {
        get { return _paths.ConfigRoot; }
    }

    public string LogsDirectory
    {
        get { return _paths.LogsDirectory; }
    }

    public string InstancesDirectory
    {
        get { return _paths.InstancesDirectory; }
    }

    public string VersionText
    {
        get { return AppVersion.Current; }
    }

    [ObservableProperty]
    public partial string UpdateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UpdateUrl { get; set; } = string.Empty;

    public string UpdateCheckLabel
    {
        get { return Text("settings.checkUpdates"); }
    }

    public string UpdateOpenLabel
    {
        get { return Text("settings.openRelease"); }
    }

    public bool HasUpdateUrl
    {
        get { return UpdateUrl.Length > 0; }
    }

    /// <summary>Asks GitHub; never automatic, because a launcher that phones home on start is a launcher nobody trusts.</summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_updates is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _updates.CheckAsync(cancellationToken);
            if (result.Failed)
            {
                UpdateText = result.Reason ?? Text("forge.offline");
                UpdateUrl = string.Empty;
            }
            else if (result.Latest is { } latest)
            {
                UpdateText = result.HasUpdate ? Localizer.Format("settings.updateAvailable", latest.Tag) : Text("settings.upToDate");
                UpdateUrl = result.HasUpdate ? latest.Url : string.Empty;
            }

            OnPropertyChanged(nameof(HasUpdateUrl));
        });
    }

    [RelayCommand]
    private void OpenRelease()
    {
        if (UpdateUrl.Length > 0)
        {
            _links?.Open(UpdateUrl);
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            var updated = _settings.Current with
            {
                Language = SelectedLanguage?.Code ?? _settings.Current.Language,
                Theme = SelectedTheme?.Value ?? _settings.Current.Theme,
                InstancesRoot = string.IsNullOrWhiteSpace(InstancesRoot) ? null : InstancesRoot.Trim(),
                MaxConcurrentDownloads = Math.Max(1, MaxConcurrentDownloads),
                LogRetentionDays = Math.Max(1, LogRetentionDays),
                LogRetentionFiles = Math.Max(1, LogRetentionFiles),
                GameStartWindow = SelectedWindow?.Value ?? _settings.Current.GameStartWindow,
                AskBeforeStoppingProcesses = AskBeforeStoppingProcesses,
            };

            await _settings.SaveAsync(updated, cancellationToken);
            Localizer.SetLanguage(updated.Language);
            _theme.Apply(updated.Theme);
            Status = Text("settings.saved");
        });
    }

    /// <summary>Fills the form from the stored settings.</summary>
    public void Apply(LauncherSettings settings)
    {
        SelectedLanguage = Languages.FirstOrDefault(language => language.Code == settings.Language) ?? Languages[0];
        SelectedTheme = Themes.FirstOrDefault(theme => theme.Value == settings.Theme) ?? Themes[0];
        SelectedWindow = Windows.FirstOrDefault(window => window.Value == settings.GameStartWindow) ?? Windows[0];
        InstancesRoot = settings.InstancesRoot ?? string.Empty;
        MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
        LogRetentionDays = settings.LogRetentionDays;
        LogRetentionFiles = settings.LogRetentionFiles;
        AskBeforeStoppingProcesses = settings.AskBeforeStoppingProcesses;
    }
}

/// <summary>A language in the picker.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>A theme in the picker; <c>Value</c> is what the setting stores.</summary>
public sealed record ThemeOption(string Value, string Name);

/// <summary>What the window does when the game starts.</summary>
public sealed record WindowOption(GameStartWindowAction Value, string Name);
