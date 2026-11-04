using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Services;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The window: a rail on the left, the page in the middle, the launch panel and the status bar. The shell owns the one
/// navigation decision (which section is shown) and hands that to the pages through <see cref="INavigationService" />.
/// </summary>
public partial class ShellViewModel : ViewModelBase, INavigationService
{
    private readonly Dictionary<LauncherSection, PageViewModel> _pages;

    public ShellViewModel(
        ILocalizer localizer,
        IInstanceContext instances,
        InstancesPageViewModel instancesPage,
        ForgePageViewModel forgePage,
        ModsPageViewModel modsPage,
        ServerPageViewModel serverPage,
        LogsPageViewModel logsPage,
        SettingsPageViewModel settingsPage,
        StatusBarViewModel statusBar,
        LaunchPanelViewModel launchPanel,
        ShellNavigation navigation
    )
        : base(localizer)
    {
        Instances = instances;
        InstancesPage = instancesPage;
        ForgePage = forgePage;
        ModsPage = modsPage;
        ServerPage = serverPage;
        LogsPage = logsPage;
        SettingsPage = settingsPage;
        StatusBar = statusBar;
        LaunchPanel = launchPanel;

        _pages = new Dictionary<LauncherSection, PageViewModel>
        {
            [LauncherSection.Instances] = instancesPage,
            [LauncherSection.Forge] = forgePage,
            [LauncherSection.Mods] = modsPage,
            [LauncherSection.Server] = serverPage,
            [LauncherSection.Logs] = logsPage,
            [LauncherSection.Settings] = settingsPage,
        };

        NavItems =
        [
            new NavItemViewModel(localizer, LauncherSection.Forge, "⌕", "nav.forge"),
            new NavItemViewModel(localizer, LauncherSection.Mods, "▦", "nav.mods"),
            new NavItemViewModel(localizer, LauncherSection.Server, "◉", "nav.server"),
            new NavItemViewModel(localizer, LauncherSection.Logs, "≡", "nav.logs"),
            new NavItemViewModel(localizer, LauncherSection.Settings, "⚙", "nav.settings"),
        ];

        navigation.Requested += (_, section) => Navigate(section);

        instances.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(ManagerTitle));
            LaunchPanel.Refresh();
            StatusBar.Refresh();
        };

        Navigate(LauncherSection.Mods);
    }

    public IInstanceContext Instances { get; }

    public InstancesPageViewModel InstancesPage { get; }

    public ForgePageViewModel ForgePage { get; }

    public ModsPageViewModel ModsPage { get; }

    public ServerPageViewModel ServerPage { get; }

    public LogsPageViewModel LogsPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public StatusBarViewModel StatusBar { get; }

    public LaunchPanelViewModel LaunchPanel { get; }

    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    [ObservableProperty]
    public partial PageViewModel CurrentPage { get; set; }

    [ObservableProperty]
    public partial LauncherSection SelectedSection { get; set; }

    public string Title
    {
        get { return Text("app.title"); }
    }

    /// <summary>Title of the manager window: the instance it manages, so two open windows can be told apart.</summary>
    public string ManagerTitle
    {
        get
        {
            var active = Instances.Active;
            return active is null ? Text("app.title") : $"{Text("app.title")} — {active.Name}";
        }
    }

    /// <summary>Shows a section and lets it load, which is how a page refreshes when it becomes visible.</summary>
    public void Navigate(LauncherSection section)
    {
        if (!_pages.TryGetValue(section, out var page))
        {
            return;
        }

        SelectedSection = section;
        foreach (var item in NavItems)
        {
            item.IsSelected = item.Section == section;
        }

        CurrentPage = page;
        _ = LoadAsync(page);
    }

    /// <summary>True when this instance's server or game is still running, which is what the close question asks.</summary>
    public bool HasRunningProcesses()
    {
        return LaunchPanel.HasRunningProcesses();
    }

    /// <summary>The rail's command: the same switch as <see cref="Navigate" />, reachable from XAML.</summary>
    [RelayCommand]
    private void Go(LauncherSection section)
    {
        Navigate(section);
    }

    /// <summary>Loads the window: the instance list first, then whatever page is showing.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await Instances.RefreshAsync(cancellationToken);
        await LaunchPanel.SyncProfileAsync(cancellationToken);
        StatusBar.Refresh();
        await LoadAsync(CurrentPage);
    }

    private static async Task LoadAsync(PageViewModel page)
    {
        switch (page)
        {
            case InstancesPageViewModel instances:
                await instances.LoadAsync(CancellationToken.None);
                break;
            case ModsPageViewModel mods:
                await mods.LoadAsync(CancellationToken.None);
                break;
            case ForgePageViewModel forge:
                await forge.LoadAsync(CancellationToken.None);
                break;
            case ServerPageViewModel server:
                await server.RefreshAsync(CancellationToken.None);
                break;
            case LogsPageViewModel logs:
                await logs.RefreshAsync(CancellationToken.None);
                break;
            case PatchPageViewModel patch:
                await patch.PlanAsync(CancellationToken.None);
                break;
            default:
                break;
        }
    }
}
