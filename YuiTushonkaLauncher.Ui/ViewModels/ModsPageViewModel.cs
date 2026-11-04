using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Services;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The installed mods of the active instance, plus the three tabs that hang off them: addons, configs and the details
/// of one selected mod. Rows are reused when the filter changes, so a 1000-entry list costs nothing per keystroke.
/// </summary>
public partial class ModsPageViewModel : PageViewModel
{
    private readonly IInstanceContext _context;
    private readonly IInstanceService _instances;
    private readonly IModInstaller _installer;
    private readonly IDialogService _dialogs;
    private readonly IDownloadActivity _downloads;
    private List<ModRowViewModel> _all = [];

    public ModsPageViewModel(
        ILocalizer localizer,
        IInstanceContext context,
        IInstanceService instances,
        IModInstaller installer,
        IDialogService dialogs,
        IDownloadActivity downloads
    )
        : base(localizer)
    {
        _context = context;
        _instances = instances;
        _installer = installer;
        _dialogs = dialogs;
        _downloads = downloads;
        _context.Changed += (_, _) => _ = LoadAsync(CancellationToken.None);
    }

    /// <summary>The rows the list shows (filtered).</summary>
    public ObservableCollection<ModRowViewModel> Mods { get; } = [];

    /// <summary>Addons of the mods above, kept in their own tab as the phase asks.</summary>
    public ObservableCollection<ModRowViewModel> Addons { get; } = [];

    /// <summary>Every config file any installed mod placed.</summary>
    public ObservableCollection<ConfigRowViewModel> Configs { get; } = [];

    [ObservableProperty]
    public partial ModRowViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UrlToInstall { get; set; } = string.Empty;

    public string Title
    {
        get { return Text("mods.title"); }
    }

    public string EmptyText
    {
        get { return Text("mods.empty"); }
    }

    public string EmptyHint
    {
        get { return Text("mods.emptyHint"); }
    }

    public string SearchLabel
    {
        get { return Text("mods.search"); }
    }

    public string InstallFileLabel
    {
        get { return Text("mods.installFile"); }
    }

    public string InstallUrlLabel
    {
        get { return Text("mods.installUrl"); }
    }

    public string InstallLabel
    {
        get { return Text("common.install"); }
    }

    public string UninstallLabel
    {
        get { return Text("common.uninstall"); }
    }

    public string PinLabel
    {
        get { return Selected?.IsPinned == true ? Text("mods.unpin") : Text("mods.pin"); }
    }

    public string CountText
    {
        get { return Localizer.Format("mods.count", Mods.Count); }
    }

    public bool HasMods
    {
        get { return Mods.Count > 0; }
    }

    public bool HasAddons
    {
        get { return Addons.Count > 0; }
    }

    public bool HasConfigs
    {
        get { return Configs.Count > 0; }
    }

    public bool CanActOnSelection
    {
        get { return Selected is not null && !IsBusy; }
    }

    public string ConfigsEmptyText
    {
        get { return Text("mods.configsEmpty"); }
    }

    /// <summary>Reads the instance's installed mods and rebuilds every tab.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            _all = [];
            ApplyFilter();
            Addons.Clear();
            Configs.Clear();
            return;
        }

        await RunAsync(() => LoadCoreAsync(instanceId, cancellationToken));
    }

    /// <summary>
    /// Re-reads the mods. Kept apart from <see cref="LoadAsync" /> because an install or uninstall already runs inside
    /// the busy guard: nesting two guarded operations would make the inner one refuse and the list would go stale.
    /// </summary>
    private async Task LoadCoreAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        await _context.RefreshAsync(cancellationToken);
        var active = await _instances.GetAsync(instanceId, cancellationToken);
        _all =
        [
            .. (active?.InstalledMods ?? [])
                .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(mod => new ModRowViewModel(Localizer, mod)),
        ];
        ApplyFilter();
        RebuildSideTabs(active);
    }

    partial void OnSearchChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedChanged(ModRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanActOnSelection));
        OnPropertyChanged(nameof(PinLabel));
    }

    [RelayCommand]
    private async Task InstallFromFileAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            Error = Text("errors.noInstance");
            return;
        }

        var path = await _dialogs.PickFileAsync(InstallFileLabel, "Archives", "*.zip", cancellationToken);
        if (path is null)
        {
            return;
        }

        await InstallAsync(new ModInstallRequest { InstanceId = instanceId, ArchivePath = path }, cancellationToken);
    }

    [RelayCommand]
    private async Task InstallFromUrlAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            Error = Text("errors.noInstance");
            return;
        }

        if (
            !Uri.TryCreate(UrlToInstall.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        )
        {
            Error = Localizer.Format("errors.generic", UrlToInstall);
            return;
        }

        await InstallAsync(new ModInstallRequest { InstanceId = instanceId, DownloadUrl = url }, cancellationToken);
        UrlToInstall = string.Empty;
    }

    [RelayCommand]
    private async Task UninstallAsync(ModRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null || _context.ActiveId is not { } instanceId)
        {
            return;
        }

        var question = new ConfirmDialogViewModel(Localizer)
        {
            Title = Text("mods.removeTitle"),
            Body = Text("mods.removeBody"),
            ConfirmLabel = Text("common.uninstall"),
        };

        if (!await _dialogs.ConfirmAsync(question, cancellationToken))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _installer.UninstallAsync(instanceId, row.Guid, cancellationToken: cancellationToken);
            Status = Localizer.Format("mods.removed", row.Name);
            await LoadCoreAsync(instanceId, cancellationToken);
        });
    }

    [RelayCommand]
    private async Task TogglePinAsync(ModRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null || _context.ActiveId is not { } instanceId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _installer.SetPinnedAsync(instanceId, row.Guid, !row.IsPinned, cancellationToken);
            await LoadCoreAsync(instanceId, cancellationToken);
        });
    }

    private async Task InstallAsync(ModInstallRequest request, CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            _downloads.Begin();
            try
            {
                var result = await _installer.InstallAsync(request, cancellationToken);
                Status = Localizer.Format("mods.installed", result.Name, result.Version.ToString());
                if (result.Warnings.Count > 0)
                {
                    Status += " " + string.Join(" ", result.Warnings);
                }
            }
            finally
            {
                _downloads.End();
            }

            await LoadCoreAsync(_context.ActiveId!.Value, cancellationToken);
        });
    }

    /// <summary>Rebuilds the visible list from the cached rows: only the collection contents change, not the rows.</summary>
    private void ApplyFilter()
    {
        var needle = Search.Trim();
        var matches = _all.Where(row => !row.IsAddon).Where(row => needle.Length == 0 || row.Matches(Search)).ToList();

        Mods.Clear();
        foreach (var row in matches)
        {
            Mods.Add(row);
        }

        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(CountText));
    }

    private void RebuildSideTabs(Instance? instance)
    {
        Addons.Clear();
        Configs.Clear();
        foreach (var addon in _all.Where(row => row.IsAddon))
        {
            Addons.Add(addon);
        }

        foreach (var (row, path) in _all.SelectMany(row => row.ConfigFiles.Select(path => (row, path))))
        {
            Configs.Add(new ConfigRowViewModel(Localizer, row.Name, path));
        }

        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(HasConfigs));
    }
}
