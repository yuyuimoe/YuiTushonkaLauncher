using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The window the launcher opens on: the instance list, and the one thing the user came for, which is starting one.
/// Everything specific to a single instance lives in the manager window, opened from the details pane.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly IWindowController _windows;

    public HomeViewModel(
        ILocalizer localizer,
        InstancesPageViewModel instancesPage,
        LaunchPanelViewModel launchPanel,
        StatusBarViewModel statusBar,
        IWindowController windows
    )
        : base(localizer)
    {
        Instances = instancesPage;
        LaunchPanel = launchPanel;
        StatusBar = statusBar;
        _windows = windows;

        instancesPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InstancesPageViewModel.Selected))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedName));
            }
        };
    }

    public InstancesPageViewModel Instances { get; }

    public LaunchPanelViewModel LaunchPanel { get; }

    public StatusBarViewModel StatusBar { get; }

    public string Title
    {
        get { return Text("app.title"); }
    }

    public string ManageLabel
    {
        get { return Text("home.manage"); }
    }

    public string SettingsLabel
    {
        get { return Text("home.settings"); }
    }

    public string DetailsLabel
    {
        get { return Text("home.details"); }
    }

    public string SelectHint
    {
        get { return Text("home.selectHint"); }
    }

    public bool HasSelection
    {
        get { return Instances.Selected is not null; }
    }

    public string SelectedName
    {
        get { return Instances.Selected?.Name ?? string.Empty; }
    }

    /// <summary>Opens the manager window on the selected instance, which is where its mods, server and logs live.</summary>
    [RelayCommand]
    private void Manage()
    {
        if (Instances.Selected is null)
        {
            return;
        }

        _windows.OpenManager(LauncherSection.Mods);
    }

    /// <summary>Settings belong to the launcher, not to an instance, so the manager opens on them from here.</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        _windows.OpenManager(LauncherSection.Settings);
    }

    /// <summary>Loads the list; the window calls this once it is shown.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await Instances.LoadAsync(cancellationToken);
        await LaunchPanel.SyncProfileAsync(cancellationToken);
        StatusBar.Refresh();
        OnPropertyChanged(nameof(HasSelection));
    }
}
