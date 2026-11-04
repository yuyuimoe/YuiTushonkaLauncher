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
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// Installed addons, which are not mods: an addon adds to a mod that must already be there, so this page is grouped by
/// the parent it belongs to. Installing one opens the Forge window in addon mode, which checks the parent first.
/// </summary>
public partial class AddonsPageViewModel : PageViewModel
{
    private readonly IInstanceContext _context;
    private readonly IInstanceService _instances;
    private readonly IWindowController _windows;

    public AddonsPageViewModel(ILocalizer localizer, IInstanceContext context, IInstanceService instances, IWindowController windows)
        : base(localizer)
    {
        _context = context;
        _instances = instances;
        _windows = windows;
        _context.Changed += (_, _) => _ = LoadAsync(CancellationToken.None);
    }

    /// <summary>The addons, grouped by the mod they extend.</summary>
    public ObservableCollection<AddonGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    public string Title
    {
        get { return Text("addons.title"); }
    }

    public string InstallLabel
    {
        get { return Text("addons.install"); }
    }

    public string EmptyText
    {
        get { return Text("addons.empty"); }
    }

    public string EmptyHint
    {
        get { return Text("addons.emptyHint"); }
    }

    public string CountText
    {
        get { return Localizer.Format("addons.count", Groups.Sum(group => group.Addons.Count)); }
    }

    public bool HasAddons
    {
        get { return Groups.Count > 0; }
    }

    /// <summary>Reads the active instance's addons and groups them under their parents.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Groups.Clear();
            if (_context.ActiveId is not { } instanceId)
            {
                OnPropertyChanged(nameof(HasAddons));
                OnPropertyChanged(nameof(CountText));
                return;
            }

            var instance = await _instances.GetAsync(instanceId, cancellationToken);
            if (instance is null)
            {
                return;
            }

            var byGuid = instance
                .InstalledMods.Where(row => !row.IsAddon)
                .ToDictionary(row => row.Guid, row => row, StringComparer.OrdinalIgnoreCase);

            foreach (var group in instance.InstalledMods.Where(row => row.IsAddon).GroupBy(row => row.ParentModGuid ?? string.Empty))
            {
                var parent = byGuid.TryGetValue(group.Key, out var found) ? found : null;
                Groups.Add(
                    new AddonGroupViewModel(
                        parent?.Name ?? (string.IsNullOrEmpty(group.Key) ? Text("forge.parentUnknown") : group.Key),
                        parent?.Version.ToString() ?? string.Empty,
                        [
                            .. group
                                .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                                .Select(row => new AddonRowViewModel(Localizer, row)),
                        ]
                    )
                );
            }

            OnPropertyChanged(nameof(HasAddons));
            OnPropertyChanged(nameof(CountText));
            Status = Groups.Count == 0 ? EmptyText : string.Empty;
        });
    }

    /// <summary>Opens the Forge window in addon mode, where a parent has to be chosen before an addon can be installed.</summary>
    [RelayCommand]
    private void InstallAddons()
    {
        if (_context.ActiveId is null)
        {
            Error = Text("errors.noInstance");
            return;
        }

        _windows.OpenForge(ForgeBrowserMode.Addons);
    }
}

/// <summary>One parent mod and the addons installed on top of it.</summary>
public sealed class AddonGroupViewModel(string parentName, string parentVersion, IReadOnlyList<AddonRowViewModel> addons)
{
    public string ParentName { get; } = parentName;

    public string ParentVersion { get; } = parentVersion;

    public IReadOnlyList<AddonRowViewModel> Addons { get; } = addons;

    public string HeaderText
    {
        get { return string.IsNullOrEmpty(ParentVersion) ? ParentName : $"{ParentName} · {ParentVersion}"; }
    }
}

/// <summary>One installed addon.</summary>
public sealed class AddonRowViewModel(ILocalizer localizer, InstalledMod addon) : ViewModelBase(localizer)
{
    public string Name
    {
        get { return addon.Name; }
    }

    public string Version
    {
        get { return addon.Version.ToString(); }
    }

    public string Guid
    {
        get { return addon.Guid; }
    }

    public string FileCountText
    {
        get { return Localizer.Format("addons.files", addon.Files.Count); }
    }
}
