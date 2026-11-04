using CommunityToolkit.Mvvm.ComponentModel;
using YuiTushonkaLauncher.Core.Abstractions;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// Phase 1 view model: proves that dependency injection, logging, settings and version stamping are
/// wired by displaying them. Replaced by the real shell in phase 12.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IAppPaths _paths;

    public MainViewModel(IAppPaths paths, ISettingsService settings)
    {
        _paths = paths;

        Greeting = "YuiTushonkaLauncher";
        Settings = settings.Current;
    }

    [ObservableProperty]
    public partial string Greeting { get; set; }

    [ObservableProperty]
    public partial Core.Configuration.LauncherSettings Settings { get; set; }

    public string Version
    {
        get { return AppVersion.Current; }
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
}
