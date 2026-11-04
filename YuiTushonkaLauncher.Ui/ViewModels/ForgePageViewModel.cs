using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Services;
using YuiTushonkaLauncher.Startup;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>Which of the sidebar's tabs is showing.</summary>
public enum ForgeDetailTab
{
    Details,
    Dependencies,
    Dependents,
}

/// <summary>
/// The Forge browser: search and ordering on top, results with their thumbnails on the left, and the selected mod's
/// sidebar on the right — details, dependencies and dependents as three tabs. Selecting a dependency is navigation, so
/// the sidebar keeps a back and forward history the way a browser does.
/// </summary>
public partial class ForgePageViewModel : PageViewModel
{
    private readonly IForgeClient _forge;
    private readonly IModInstaller _installer;
    private readonly IInstanceService _instances;
    private readonly IInstanceContext _context;
    private readonly IDownloadActivity _downloads;
    private readonly IDialogService _dialogs;
    private readonly IThumbnailCache _thumbnails;
    private readonly IForgePageReader _pages;
    private readonly IUrlOpener _urls;
    private readonly IAddonService _addons;
    private readonly TimeProvider _time;
    private readonly NavigationHistory<int> _history = new();
    private readonly Dictionary<int, ForgeModPageInfo?> _pageCache = [];

    /// <summary>Set while a row is restored from the history, so the selection does not record a new step.</summary>
    private bool _restoring;

    public ForgePageViewModel(
        ILocalizer localizer,
        IForgeClient forge,
        IModInstaller installer,
        IInstanceService instances,
        IInstanceContext context,
        IDownloadActivity downloads,
        IDialogService dialogs,
        IThumbnailCache thumbnails,
        IForgePageReader pages,
        IUrlOpener urls,
        IAddonService addons,
        TimeProvider time
    )
        : base(localizer)
    {
        _forge = forge;
        _installer = installer;
        _instances = instances;
        _context = context;
        _downloads = downloads;
        _dialogs = dialogs;
        _thumbnails = thumbnails;
        _pages = pages;
        _urls = urls;
        _addons = addons;
        _time = time;

        foreach (var choice in ForgeSorts.All)
        {
            Sorts.Add(new ForgeSortOption(localizer, choice));
        }

        SelectedSort = Sorts[0];
        _context.Changed += (_, _) => OnInstanceChanged();
    }

    /// <summary>The ordering choices, in the order the picker shows them.</summary>
    public ObservableCollection<ForgeSortOption> Sorts { get; } = [];

    public ObservableCollection<ForgeModRowViewModel> Results { get; } = [];

    public ObservableCollection<ForgeCategoryOption> Categories { get; } = [];

    public ObservableCollection<ForgeVersionRowViewModel> Versions { get; } = [];

    public ObservableCollection<ForgeModReferenceViewModel> Dependencies { get; } = [];

    public ObservableCollection<ForgeModReferenceViewModel> Dependents { get; } = [];

    public ObservableCollection<ForgeLinkViewModel> SourceLinks { get; } = [];

    public ObservableCollection<ForgeLinkViewModel> ScanLinks { get; } = [];

    /// <summary>The addons of the chosen parent, when this window is in addon mode.</summary>
    public ObservableCollection<ForgeAddonRowViewModel> AddonResults { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ForgeSortOption? SelectedSort { get; set; }

    [ObservableProperty]
    public partial ForgeCategoryOption? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial bool FeaturedOnly { get; set; }

    [ObservableProperty]
    public partial bool FikaOnly { get; set; }

    /// <summary>The row selected in the list; keeping the details and the list in step is what this is for.</summary>
    [ObservableProperty]
    public partial ForgeModRowViewModel? Selected { get; set; }

    /// <summary>The mod whose details are showing, which may have been reached from a dependency row.</summary>
    [ObservableProperty]
    public partial ForgeModRowViewModel? Shown { get; set; }

    [ObservableProperty]
    public partial ForgeVersionRowViewModel? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial ForgeDetailTab ActiveTab { get; set; } = ForgeDetailTab.Details;

    /// <summary>Whether this window is looking for a mod or for an addon of one.</summary>
    [ObservableProperty]
    public partial ForgeBrowserMode Mode { get; set; } = ForgeBrowserMode.Mods;

    /// <summary>The mod an addon would belong to, chosen from the results.</summary>
    [ObservableProperty]
    public partial ForgeModRowViewModel? Parent { get; set; }

    /// <summary>What is needed before an addon can be installed, in the colour the user asked for.</summary>
    [ObservableProperty]
    public partial string RequirementText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRequirementMet { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LicenseText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScanSummary { get; set; } = string.Empty;

    public string Title
    {
        get { return Text(Mode == ForgeBrowserMode.Addons ? "forge.addonsTitle" : "forge.title"); }
    }

    /// <summary>True when the window is looking for addons, which changes what the buttons mean.</summary>
    public bool IsAddonMode
    {
        get { return Mode == ForgeBrowserMode.Addons; }
    }

    public string UseAsParentLabel
    {
        get { return Text("forge.useAsParent"); }
    }

    public string ParentLabel
    {
        get { return Text("forge.parent"); }
    }

    public string InstallAddonLabel
    {
        get { return Text("forge.installAddon"); }
    }

    public bool HasAddonResults
    {
        get { return AddonResults.Count > 0; }
    }

    public bool HasParent
    {
        get { return Parent is not null; }
    }

    partial void OnModeChanged(ForgeBrowserMode value)
    {
        OnPropertyChanged(nameof(IsAddonMode));
        OnPropertyChanged(nameof(Title));
        if (value == ForgeBrowserMode.Mods)
        {
            AddonResults.Clear();
            Parent = null;
            OnPropertyChanged(nameof(HasParent));
        }
    }

    public string SearchPlaceholder
    {
        get { return Text("forge.searchPlaceholder"); }
    }

    public string SearchButtonLabel
    {
        get { return Text("forge.search"); }
    }

    public string SortLabel
    {
        get { return Text("forge.sort"); }
    }

    public string CategoryLabel
    {
        get { return Text("forge.category"); }
    }

    public string FeaturedLabel
    {
        get { return Text("forge.featured"); }
    }

    public string FikaLabel
    {
        get { return Text("forge.fika"); }
    }

    public string DetailsTabLabel
    {
        get { return Text("forge.tabDetails"); }
    }

    public string DependenciesTabLabel
    {
        get { return Text("forge.tabDependencies"); }
    }

    public string DependentsTabLabel
    {
        get { return Text("forge.tabDependents"); }
    }

    public string SelectPrompt
    {
        get { return Text("forge.selectPrompt"); }
    }

    public string VersionsLabel
    {
        get { return Text("forge.versions"); }
    }

    public string InstallLabel
    {
        get { return Text("common.install"); }
    }

    public string OpenInSiteLabel
    {
        get { return Text("forge.openInSite"); }
    }

    /// <summary>The two navigation arrows, which are symbols rather than translatable text.</summary>
    public string BackGlyph
    {
        get { return "◀"; }
    }

    public string ForwardGlyph
    {
        get { return "▶"; }
    }

    public string BackLabel
    {
        get { return Text("common.back"); }
    }

    public string ForwardLabel
    {
        get { return Text("common.forward"); }
    }

    public string ShortDescriptionLabel
    {
        get { return Text("forge.shortDescription"); }
    }

    public string LongDescriptionLabel
    {
        get { return Text("forge.longDescription"); }
    }

    public string SourceCodeLabel
    {
        get { return Text("forge.sourceCode"); }
    }

    public string VirusTotalLabel
    {
        get { return Text("forge.virusTotal"); }
    }

    public string NoDependenciesText
    {
        get { return Text("forge.noDependencies"); }
    }

    public string NoDependentsText
    {
        get { return Text("forge.noDependents"); }
    }

    public string ResultsText
    {
        get { return Localizer.Format("forge.results", Results.Count); }
    }

    public string EmptyText
    {
        get { return Text("forge.empty"); }
    }

    public bool HasResults
    {
        get { return Results.Count > 0; }
    }

    public bool HasSelection
    {
        get { return Shown is not null; }
    }

    public bool IsDetailsTab
    {
        get { return ActiveTab == ForgeDetailTab.Details; }
    }

    public bool IsDependenciesTab
    {
        get { return ActiveTab == ForgeDetailTab.Dependencies; }
    }

    public bool IsDependentsTab
    {
        get { return ActiveTab == ForgeDetailTab.Dependents; }
    }

    public bool HasDependencies
    {
        get { return Dependencies.Count > 0; }
    }

    public bool HasDependents
    {
        get { return Dependents.Count > 0; }
    }

    public bool HasSourceLinks
    {
        get { return SourceLinks.Count > 0; }
    }

    public bool HasScanLinks
    {
        get { return ScanLinks.Count > 0; }
    }

    public bool HasLicense
    {
        get { return !string.IsNullOrEmpty(LicenseText); }
    }

    public bool CanGoBack
    {
        get { return _history.CanGoBack; }
    }

    public bool CanGoForward
    {
        get { return _history.CanGoForward; }
    }

    /// <summary>
    /// The sidebar load that is in flight, so a caller that has to wait for it (a test, or a page that wants to swallow
    /// the result) can await the work a selection started.
    /// </summary>
    public Task DetailsLoading { get; private set; } = Task.CompletedTask;

    /// <summary>True when at least one published version fits the active instance's SPT release.</summary>
    public bool CanInstall
    {
        get { return Versions.Count > 0 && _context.ActiveId is not null; }
    }

    /// <summary>Why the install button is off, phrased for the user; empty when it is on.</summary>
    public string InstallBlocker
    {
        get
        {
            if (Shown is null)
            {
                return string.Empty;
            }

            if (_context.Active is not { } instance)
            {
                return Text("errors.noInstance");
            }

            if (Versions.Count == 0)
            {
                return Localizer.Format("forge.noCompatibleVersion", instance.SptVersion.ToString());
            }

            return string.Empty;
        }
    }

    public string SptVersionText
    {
        get
        {
            return _context.Active is { } instance
                ? Localizer.Format("instances.sptVersion", instance.SptVersion.ToString())
                : string.Empty;
        }
    }

    /// <summary>Loads the category list once, so the filter is usable before the first search.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Categories.Count > 0)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var categories = await _forge.GetCategoriesAsync(cancellationToken);
            Categories.Clear();
            foreach (var category in categories.OrderBy(category => category.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                Categories.Add(new ForgeCategoryOption(category.Id, category.Title));
            }

            OnPropertyChanged(nameof(SptVersionText));
        });
    }

    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            var query = ForgeSorts.Apply(
                new ForgeModQuery
                {
                    Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    CategoryId = SelectedCategory?.Id,
                    SptVersion = _context.Active?.SptVersion,
                    IsFeatured = FeaturedOnly ? true : null,
                    FikaCompatibility = FikaOnly ? "compatible" : null,
                    IncludeVersions = true,
                },
                SelectedSort?.Sort ?? ForgeSort.Featured,
                _time.GetUtcNow()
            );

            var page = await _forge.GetModsAsync(query, cancellationToken);
            Results.Clear();
            foreach (var mod in page.Items)
            {
                Results.Add(new ForgeModRowViewModel(Localizer, mod));
            }

            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(HasResults));
            if (Results.Count == 0)
            {
                Status = EmptyText;
            }

            LoadThumbnails();
        });
    }

    /// <summary>Re-runs the search with the current ordering, which is what changing the sort does.</summary>
    partial void OnSelectedSortChanged(ForgeSortOption? value)
    {
        if (value is not null && Results.Count > 0)
        {
            _ = SearchAsync(CancellationToken.None);
        }
    }

    partial void OnSelectedChanged(ForgeModRowViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        // Restoring a row from the history must not push it again, or Back would walk in circles.
        var record = !_restoring;
        _restoring = false;
        if (record)
        {
            _history.Push(value.Id);
            NotifyHistory();
        }

        DetailsLoading = LoadDetailsAsync(value, CancellationToken.None);
    }

    partial void OnActiveTabChanged(ForgeDetailTab value)
    {
        OnPropertyChanged(nameof(IsDetailsTab));
        OnPropertyChanged(nameof(IsDependenciesTab));
        OnPropertyChanged(nameof(IsDependentsTab));
    }

    [RelayCommand]
    private void ShowTab(ForgeDetailTab tab)
    {
        ActiveTab = tab;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_history.Back() is { } id)
        {
            DetailsLoading = NavigateToAsync(id, recordHistory: false, CancellationToken.None);
            NotifyHistory();
        }
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_history.Forward() is { } id)
        {
            DetailsLoading = NavigateToAsync(id, recordHistory: false, CancellationToken.None);
            NotifyHistory();
        }
    }

    /// <summary>Opens a dependency or dependant: the same navigation a double-click in the list performs.</summary>
    [RelayCommand]
    private void Navigate(ForgeModReferenceViewModel? reference)
    {
        if (reference is not null)
        {
            DetailsLoading = NavigateToAsync(reference.ModId, recordHistory: true, CancellationToken.None);
        }
    }

    /// <summary>Addon mode: this result becomes the parent whose addons are listed.</summary>
    [RelayCommand]
    private async Task UseAsParentAsync(ForgeModRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null)
        {
            return;
        }

        Parent = row;
        OnPropertyChanged(nameof(HasParent));
        DetailsLoading = LoadAddonsAsync(row, cancellationToken);
        await DetailsLoading;
    }

    /// <summary>Installs one addon next to its parent, through the addon service that owns the constraint check.</summary>
    [RelayCommand]
    private async Task InstallAddonAsync(ForgeAddonRowViewModel? row, CancellationToken cancellationToken)
    {
        if (Parent is not { } parent || row is null)
        {
            return;
        }

        if (_context.ActiveId is not { } instanceId)
        {
            Error = Text("errors.noInstance");
            return;
        }

        if (!row.CanInstall)
        {
            Error = row.RequirementText;
            return;
        }

        await RunAsync(async () =>
        {
            _downloads.Begin();
            try
            {
                var result = await _addons.InstallAsync(
                    new AddonInstallRequest
                    {
                        InstanceId = instanceId,
                        ParentModGuid = parent.Guid ?? string.Empty,
                        ForgeId = row.Id,
                        DownloadUrl = new Uri(row.DownloadUrl),
                        Version = row.Version,
                        ModVersionConstraint = row.ModConstraint,
                    },
                    cancellationToken
                );

                Status = Localizer.Format("forge.addonInstalled", result.Guid, result.Version.ToString());
                await LoadAddonsAsync(parent, cancellationToken);
            }
            finally
            {
                _downloads.End();
            }
        });
    }

    /// <summary>Lists a parent's addons and marks each one with whether it can be installed here.</summary>
    private async Task LoadAddonsAsync(ForgeModRowViewModel parent, CancellationToken cancellationToken)
    {
        AddonResults.Clear();
        RequirementText = string.Empty;
        IsRequirementMet = false;
        OnPropertyChanged(nameof(IsRequirementMet));
        OnPropertyChanged(nameof(HasAddonResults));

        if (parent.Guid is not { Length: > 0 } parentGuid)
        {
            RequirementText = Text("forge.parentUnknown");
            return;
        }

        var installed = _context.ActiveId is { } instanceId ? await _instances.GetAsync(instanceId, cancellationToken) : null;
        var installedParent = installed?.InstalledMods.FirstOrDefault(row =>
            string.Equals(row.Guid, parentGuid, StringComparison.OrdinalIgnoreCase)
        );

        IsRequirementMet = installedParent is not null;
        RequirementText = installedParent is { } found
            ? Localizer.Format("forge.parentInstalled", parent.Name, found.Version.ToString())
            : Localizer.Format("forge.parentMissing", parent.Name);
        OnPropertyChanged(nameof(IsRequirementMet));
        OnPropertyChanged(nameof(RequirementText));

        var page = await _forge.GetAddonsAsync(new ForgeAddonQuery { ModId = parent.Id, IncludeVersions = true }, cancellationToken);
        foreach (var addon in page.Items)
        {
            var versions = (await _forge.GetAddonVersionsAsync(addon.Id, cancellationToken))
                .OrderByDescending(version => version.Version)
                .ToList();
            var chosen = versions.FirstOrDefault();
            var admitted =
                chosen is null || installedParent is null
                    ? installedParent is not null
                    : chosen.ModVersionConstraint.Satisfies(installedParent.Version);

            AddonResults.Add(
                new ForgeAddonRowViewModel(
                    addon.Id,
                    addon.Name,
                    chosen?.Version ?? new SemanticVersion(0, 0, 0),
                    chosen?.ModVersionConstraint,
                    chosen?.DownloadUrl ?? string.Empty,
                    CanInstall: installedParent is not null && admitted,
                    RequirementText: installedParent is null ? Localizer.Format("forge.parentMissing", parent.Name)
                        : admitted ? Localizer.Format("forge.parentInstalled", parent.Name, installedParent.Version.ToString())
                        : Localizer.Format("forge.addonNeedsOtherParent", parent.Name, chosen!.ModVersionConstraint.ToString())
                )
            );
        }

        OnPropertyChanged(nameof(HasAddonResults));
    }

    /// <summary>Opens one of the sidebar's links in the system browser.</summary>
    [RelayCommand]
    private void OpenLink(ForgeLinkViewModel? link)
    {
        if (link is not null)
        {
            _urls.Open(link.Url);
        }
    }

    [RelayCommand]
    private void OpenInSite()
    {
        if (Shown is not null)
        {
            _urls.Open(Shown.SiteUrl);
        }
    }

    [RelayCommand]
    private async Task InstallAsync(ForgeVersionRowViewModel? version, CancellationToken cancellationToken)
    {
        if (Shown is not { } mod)
        {
            return;
        }

        if (_context.ActiveId is not { } instanceId)
        {
            Error = Text("errors.noInstance");
            return;
        }

        var chosen = version ?? SelectedVersion ?? Versions.FirstOrDefault();
        if (chosen is null)
        {
            Error = InstallBlocker;
            return;
        }

        await RunAsync(async () =>
        {
            _downloads.Begin();
            try
            {
                var result = await _installer.InstallAsync(
                    new ModInstallRequest
                    {
                        InstanceId = instanceId,
                        DownloadUrl = new Uri(chosen.DownloadUrl),
                        ForgeId = mod.Id,
                        ExpectedVersion = chosen.Row.Version,
                    },
                    cancellationToken
                );

                Status = Localizer.Format("mods.installed", result.Name, result.Version.ToString());
            }
            finally
            {
                _downloads.End();
            }
        });
    }

    /// <summary>The view's double-click: select the row, which is what loads its details.</summary>
    [RelayCommand]
    private void ShowDetails(ForgeModRowViewModel? row)
    {
        if (row is not null)
        {
            _restoring = false;
            Selected = row;
        }
    }

    /// <summary>
    /// Opens a mod from a dependency or dependant row. That is navigation, so it becomes a history step, and a step can
    /// point at a mod the result list does not contain.
    /// </summary>
    private Task NavigateToAsync(int modId, bool recordHistory, CancellationToken cancellationToken)
    {
        if (recordHistory && _history.Push(modId))
        {
            NotifyHistory();
        }

        return ShowByIdAsync(modId, cancellationToken);
    }

    /// <summary>Shows a mod by Forge id, fetching it when the result list does not hold it.</summary>
    private async Task ShowByIdAsync(int modId, CancellationToken cancellationToken)
    {
        var known = Results.FirstOrDefault(row => row.Id == modId);
        if (known is not null)
        {
            // The selection event loads the details; it must not record the step a second time.
            _restoring = true;
            Selected = known;
            return;
        }

        try
        {
            var found = await _forge.GetModAsync(modId, cancellationToken);
            var row = new ForgeModRowViewModel(Localizer, found);
            Selected = null;
            Shown = row;
            await LoadDetailsAsync(row, cancellationToken);
        }
        catch (ForgeException exception)
        {
            Error = ViewModelErrors.Describe(exception, Localizer);
        }
    }

    /// <summary>Repaints the two navigation buttons after the history moved.</summary>
    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    /// <summary>Fills the sidebar: versions that fit the instance, description, page extras, dependencies, dependents.</summary>
    private async Task LoadDetailsAsync(ForgeModRowViewModel mod, CancellationToken cancellationToken)
    {
        Shown = mod;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(InstallBlocker));
        Versions.Clear();
        Dependencies.Clear();
        Dependents.Clear();
        SourceLinks.Clear();
        ScanLinks.Clear();
        Description = string.Empty;
        LicenseText = string.Empty;
        ScanSummary = string.Empty;
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(HasDependents));
        OnPropertyChanged(nameof(HasSourceLinks));
        OnPropertyChanged(nameof(HasScanLinks));
        OnPropertyChanged(nameof(HasLicense));
        OnPropertyChanged(nameof(CanInstall));

        try
        {
            var sptVersion = _context.Active?.SptVersion;
            var published = await _forge.GetModVersionsAsync(mod.Id, cancellationToken);
            var usable = ForgeVersionCompatibility.Compatible(published, sptVersion);

            foreach (var version in usable)
            {
                Versions.Add(new ForgeVersionRowViewModel(Localizer, version));
            }

            SelectedVersion = Versions.FirstOrDefault();
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(InstallBlocker));

            var description = mod.Description ?? (await _forge.GetModAsync(mod.Id, cancellationToken)).Description;
            Description = ForgeDescription.ToText(description);

            await LoadPageExtrasAsync(mod, cancellationToken);
            Dependencies.Clear();
            foreach (var dependency in await LoadDependenciesAsync(mod, cancellationToken))
            {
                Dependencies.Add(dependency);
            }

            foreach (var dependent in await LoadDependentsAsync(mod, cancellationToken))
            {
                Dependents.Add(dependent);
            }

            OnPropertyChanged(nameof(HasDependencies));
            OnPropertyChanged(nameof(HasDependents));
        }
        catch (ForgeException exception)
        {
            // A sidebar that cannot load is a message, not a broken page.
            Error = ViewModelErrors.Describe(exception, Localizer);
        }
    }

    /// <summary>Licence, source code and scans live only on the site's page; the reader fills them when it can.</summary>
    private async Task LoadPageExtrasAsync(ForgeModRowViewModel mod, CancellationToken cancellationToken)
    {
        if (!_pageCache.TryGetValue(mod.Id, out var info))
        {
            info = await _pages.ReadAsync(mod.Id, mod.Slug, cancellationToken);
            _pageCache[mod.Id] = info;
        }

        if (info is null)
        {
            return;
        }

        LicenseText = info.License is { } license && info.LicenseUrl is { } url ? $"{license} — {url}" : info.License ?? string.Empty;
        ScanSummary = info.ScanSummary ?? string.Empty;
        foreach (var link in info.SourceCodeUrls)
        {
            SourceLinks.Add(new ForgeLinkViewModel(link));
        }

        foreach (var link in info.VirusTotalUrls)
        {
            ScanLinks.Add(new ForgeLinkViewModel(link));
        }

        OnPropertyChanged(nameof(HasLicense));
        OnPropertyChanged(nameof(HasSourceLinks));
        OnPropertyChanged(nameof(HasScanLinks));
    }

    private async Task<IReadOnlyList<ForgeModReferenceViewModel>> LoadDependenciesAsync(
        ForgeModRowViewModel mod,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(mod.Guid))
        {
            return [];
        }

        // The API rejects an SPT version nobody published for, and 0.0.0 is exactly that. Without an instance there is
        // nothing to check dependencies against, so the sidebar says "none" instead of showing an API error.
        if (_context.Active?.SptVersion is not { } sptVersion)
        {
            return [];
        }

        var packages = new List<InstalledPackage>
        {
            new() { Guid = mod.Guid, Version = mod.LatestVersion },
        };
        var dependencies = await _forge.GetDependenciesAsync(packages, sptVersion, cancellationToken);
        var found = DependenciesOf(dependencies, mod.Guid, mod.LatestVersion);
        if (found.Count == 0)
        {
            return [];
        }

        var installed = await InstalledGuidsAsync(cancellationToken);
        return
        [
            .. found
                .Where(dependency => dependency.ModId is not null)
                .Select(dependency => new ForgeModReferenceViewModel(
                    dependency.ModId!.Value,
                    dependency.Name ?? dependency.Guid ?? "?",
                    dependency.Guid,
                    dependency.Version?.ToString() ?? string.Empty,
                    dependency.IsOptional == true,
                    dependency.Guid is { } guid && installed.Contains(guid)
                )),
        ];
    }

    /// <summary>
    /// Who depends on this mod: the installed mods' dependency lists, intersected with it. The API reports dependencies
    /// per package, so the answer is already there — no second endpoint and no crawl.
    /// </summary>
    private async Task<IReadOnlyList<ForgeModReferenceViewModel>> LoadDependentsAsync(
        ForgeModRowViewModel mod,
        CancellationToken cancellationToken
    )
    {
        if (_context.ActiveId is not { } instanceId || string.IsNullOrEmpty(mod.Guid))
        {
            return [];
        }

        var instance = await _instances.GetAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            return [];
        }

        var installed = instance
            .InstalledMods.Where(row => !row.IsAddon && !string.IsNullOrEmpty(row.Guid))
            .Select(row => new InstalledPackage { Guid = row.Guid, Version = row.Version })
            .ToList();

        if (installed.Count == 0)
        {
            return [];
        }

        if (_context.Active is not { } active)
        {
            return [];
        }

        var dependencies = await _forge.GetDependenciesAsync(installed, active.SptVersion, cancellationToken);
        var dependents = new List<ForgeModReferenceViewModel>();
        foreach (var package in installed)
        {
            var found = DependenciesOf(dependencies, package.Guid, package.Version);
            if (found.Count == 0)
            {
                continue;
            }

            if (found.Any(dependency => string.Equals(dependency.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase)))
            {
                var row = instance.InstalledMods.First(candidate => candidate.Guid == package.Guid);
                dependents.Add(
                    new ForgeModReferenceViewModel(
                        row.Mod?.ForgeId ?? 0,
                        row.Name,
                        row.Guid,
                        row.Version.ToString(),
                        optional: false,
                        installed: true
                    )
                );
            }
        }

        return dependents;
    }

    /// <summary>
    /// The dependency list for one package. The API keys its answer by GUID; the composite form exists because a
    /// caller may ask for several versions, so both shapes are read here rather than assumed.
    /// </summary>
    private static IReadOnlyList<ForgeDependency> DependenciesOf(
        IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>> map,
        string guid,
        SemanticVersion version
    )
    {
        if (map.TryGetValue(guid, out var exact))
        {
            return exact;
        }

        if (map.TryGetValue($"{guid}:{version}", out var composite))
        {
            return composite;
        }

        foreach (var pair in map)
        {
            if (pair.Key.StartsWith(guid + ":", StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return [];
    }

    /// <summary>The GUIDs installed in the active instance, for the "installed" mark on a row.</summary>
    private async Task<HashSet<string>> InstalledGuidsAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var instance = await _instances.GetAsync(instanceId, cancellationToken);
        return instance is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(instance.InstalledMods.Select(row => row.Guid), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Downloads the thumbnails in the background; a row without one keeps its letter tile.</summary>
    private void LoadThumbnails()
    {
        foreach (var row in Results)
        {
            _ = row.LoadThumbnailAsync(_thumbnails);
        }
    }

    private void OnInstanceChanged()
    {
        OnPropertyChanged(nameof(SptVersionText));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallBlocker));
        if (Shown is { } shown)
        {
            DetailsLoading = LoadDetailsAsync(shown, CancellationToken.None);
        }
    }
}

/// <summary>One entry of the ordering picker.</summary>
public sealed class ForgeSortOption : ViewModelBase
{
    public ForgeSortOption(ILocalizer localizer, ForgeSortChoice choice)
        : base(localizer)
    {
        Sort = choice.Sort;
        LabelKey = choice.LabelKey;
    }

    public ForgeSort Sort { get; }

    public string LabelKey { get; }

    public string Label
    {
        get { return Text(LabelKey); }
    }

    public override string ToString()
    {
        return Label;
    }
}

/// <summary>A category in the filter box.</summary>
public sealed record ForgeCategoryOption(int Id, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}

/// <summary>One Forge result row.</summary>
public sealed class ForgeModRowViewModel : ViewModelBase
{
    public ForgeModRowViewModel(ILocalizer localizer, ForgeMod mod)
        : base(localizer)
    {
        Mod = mod;
        LatestVersion = mod.Versions.Count > 0 ? mod.Versions[0].Version : new SemanticVersion(0, 0, 0);
    }

    public ForgeMod Mod { get; }

    public int Id
    {
        get { return Mod.Id; }
    }

    public string Name
    {
        get { return Mod.Name; }
    }

    public string Slug
    {
        get { return Mod.Slug; }
    }

    public string? Guid
    {
        get { return Mod.Guid; }
    }

    public string? Description
    {
        get { return Mod.Description; }
    }

    public string AuthorText
    {
        get { return Mod.Author ?? Text("common.unknown"); }
    }

    public string TeaserText
    {
        get { return Mod.Teaser ?? string.Empty; }
    }

    public string DownloadsText
    {
        get { return Localizer.Format("forge.downloads", Mod.Downloads); }
    }

    /// <summary>Where the browser sends the user for the mod's own page, as the API reports it.</summary>
    public string SiteUrl
    {
        get { return Mod.DetailUrl ?? $"https://sp-mod.com/mod/{Mod.Id}/{Mod.Slug}"; }
    }

    /// <summary>The letter shown when there is no thumbnail, which is most of the catalogue.</summary>
    public string Initial
    {
        get { return Mod.Name.Length > 0 ? Mod.Name[..1].ToUpperInvariant() : "?"; }
    }

    /// <summary>Decoded thumbnail, or <c>null</c> for the letter tile.</summary>
    public Bitmap? Thumbnail { get; private set; }

    public bool HasThumbnail
    {
        get { return Thumbnail is not null; }
    }

    public SemanticVersion LatestVersion { get; }

    /// <summary>Fetches the thumbnail once and decodes it off the UI thread's critical path.</summary>
    public async Task LoadThumbnailAsync(IThumbnailCache cache, CancellationToken cancellationToken = default)
    {
        if (Thumbnail is not null || string.IsNullOrEmpty(Mod.ThumbnailUrl))
        {
            return;
        }

        var path = await cache.GetAsync(Mod.ThumbnailUrl, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Thumbnail = new Bitmap(stream);
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(HasThumbnail));
        }
        catch (Exception)
        {
            // A thumbnail that will not decode is a letter tile, not an error.
        }
    }
}

/// <summary>A dependency or dependant: enough to show it and to open it.</summary>
public sealed class ForgeModReferenceViewModel
{
    public ForgeModReferenceViewModel(int modId, string name, string? guid, string version, bool optional, bool installed)
    {
        ModId = modId;
        Name = name;
        Guid = guid;
        Version = version;
        Optional = optional;
        Installed = installed;
    }

    public int ModId { get; }

    public string Name { get; }

    public string? Guid { get; }

    public string Version { get; }

    public bool Optional { get; }

    public bool Installed { get; }

    public string VersionText
    {
        get { return string.IsNullOrEmpty(Version) ? string.Empty : Version; }
    }

    public string OptionalText
    {
        get { return Optional ? "(optional)" : string.Empty; }
    }
}

/// <summary>A link the page produced, opened through the system browser by the view.</summary>
public sealed class ForgeLinkViewModel(string url)
{
    public string Url { get; } = url;

    public string Label { get; } = url.Length > 60 ? "…" + url[^57..] : url;
}

/// <summary>One version of the shown mod that fits the instance, with the SPT constraint it was published with.</summary>
public sealed class ForgeVersionRowViewModel : ViewModelBase
{
    public ForgeVersionRowViewModel(ILocalizer localizer, ForgeModVersion version)
        : base(localizer)
    {
        Row = version;
    }

    public ForgeModVersion Row { get; }

    public string VersionText
    {
        get { return Row.Version.ToString(); }
    }

    public string ConstraintText
    {
        get { return Row.SptConstraint.ToString(); }
    }

    public string DownloadUrl
    {
        get { return Row.DownloadUrl; }
    }
}

/// <summary>One addon of the chosen parent, with the requirement that decides whether it can be installed.</summary>
public sealed class ForgeAddonRowViewModel(
    int id,
    string name,
    SemanticVersion version,
    VersionRange? modConstraint,
    string downloadUrl,
    bool CanInstall,
    string RequirementText
)
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public SemanticVersion Version { get; } = version;

    public VersionRange? ModConstraint { get; } = modConstraint;

    public string DownloadUrl { get; } = downloadUrl;

    /// <summary>False when the parent is missing or the addon wants another parent version.</summary>
    public bool CanInstall { get; } = CanInstall;

    /// <summary>The requirement line: green when it is met, red when it is not.</summary>
    public string RequirementText { get; } = RequirementText;
}
