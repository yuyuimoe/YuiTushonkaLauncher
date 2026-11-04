using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Services;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The Forge browser: search and filters on the left, results in the middle, the selected mod's versions and
/// dependencies on the right. Installing goes through the same <see cref="IModInstaller" /> as every other path, so
/// the SPT-version check and the archive safety rules apply here too.
/// </summary>
public partial class ForgePageViewModel : PageViewModel
{
    private readonly IForgeClient _forge;
    private readonly IModInstaller _installer;
    private readonly IInstanceContext _context;
    private readonly IDownloadActivity _downloads;
    private readonly IDialogService _dialogs;

    public ForgePageViewModel(
        ILocalizer localizer,
        IForgeClient forge,
        IModInstaller installer,
        IInstanceContext context,
        IDownloadActivity downloads,
        IDialogService dialogs
    )
        : base(localizer)
    {
        _forge = forge;
        _installer = installer;
        _context = context;
        _downloads = downloads;
        _dialogs = dialogs;
    }

    public ObservableCollection<ForgeModRowViewModel> Results { get; } = [];

    public ObservableCollection<ForgeVersionRowViewModel> Versions { get; } = [];

    public ObservableCollection<string> Dependencies { get; } = [];

    public ObservableCollection<ForgeCategoryOption> Categories { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ForgeCategoryOption? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial bool FeaturedOnly { get; set; }

    [ObservableProperty]
    public partial bool FikaOnly { get; set; }

    [ObservableProperty]
    public partial ForgeModRowViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial ForgeVersionRowViewModel? SelectedVersion { get; set; }

    public string Title
    {
        get { return Text("forge.title"); }
    }

    public string SearchPlaceholder
    {
        get { return Text("forge.searchPlaceholder"); }
    }

    public string SearchLabel
    {
        get { return Text("forge.search"); }
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

    public string DetailsLabel
    {
        get { return Text("forge.details"); }
    }

    public string SelectPrompt
    {
        get { return Text("forge.selectPrompt"); }
    }

    public string VersionsLabel
    {
        get { return Text("forge.versions"); }
    }

    public string DependenciesLabel
    {
        get { return Text("forge.dependencies"); }
    }

    public string NoDependenciesText
    {
        get { return Text("forge.noDependencies"); }
    }

    public string InstallLabel
    {
        get { return Text("common.install"); }
    }

    public string SearchButtonLabel
    {
        get { return Text("forge.search"); }
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
        get { return Selected is not null; }
    }

    public bool HasDependencies
    {
        get { return Dependencies.Count > 0; }
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
            var page = await _forge.GetModsAsync(
                new ForgeModQuery
                {
                    Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    CategoryId = SelectedCategory?.Id,
                    SptVersion = _context.Active?.SptVersion,
                    IsFeatured = FeaturedOnly ? true : null,
                    FikaCompatibility = FikaOnly ? "compatible" : null,
                },
                cancellationToken
            );

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
        });
    }

    partial void OnSelectedChanged(ForgeModRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        SelectedVersion = null;
        Versions.Clear();
        Dependencies.Clear();
        OnPropertyChanged(nameof(HasDependencies));
        if (value is not null)
        {
            _ = LoadDetailsAsync(value, CancellationToken.None);
        }
    }

    [RelayCommand]
    private async Task InstallAsync(ForgeVersionRowViewModel? version, CancellationToken cancellationToken)
    {
        if (Selected is not { } mod)
        {
            return;
        }

        if (_context.ActiveId is not { } instanceId)
        {
            Error = Text("errors.noInstance");
            return;
        }

        var chosen = version ?? Versions.FirstOrDefault();
        if (chosen is null)
        {
            Error = Text("forge.selectPrompt");
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

    private async Task LoadDetailsAsync(ForgeModRowViewModel mod, CancellationToken cancellationToken)
    {
        try
        {
            var versions = await _forge.GetModVersionsAsync(mod.Id, cancellationToken);
            var dependencies = await GetDependenciesAsync(mod, cancellationToken);

            Versions.Clear();
            foreach (var version in versions.OrderByDescending(version => version.Version))
            {
                Versions.Add(new ForgeVersionRowViewModel(Localizer, version));
            }

            Dependencies.Clear();
            foreach (var dependency in dependencies)
            {
                Dependencies.Add(dependency);
            }

            SelectedVersion = Versions.FirstOrDefault();
            OnPropertyChanged(nameof(HasDependencies));
        }
        catch (ForgeException exception)
        {
            // A detail pane that cannot load is a message, not a broken page.
            Error = ViewModelErrors.Describe(exception, Localizer);
        }
    }

    private async Task<IReadOnlyList<string>> GetDependenciesAsync(ForgeModRowViewModel mod, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(mod.Guid))
        {
            return [];
        }

        var packages = new List<InstalledPackage>
        {
            new() { Guid = mod.Guid, Version = mod.LatestVersion },
        };
        var sptVersion = _context.Active?.SptVersion ?? new SemanticVersion(0, 0, 0);
        var dependencies = await _forge.GetDependenciesAsync(packages, sptVersion, cancellationToken);
        if (!dependencies.TryGetValue(mod.Guid, out var found) || found.Count == 0)
        {
            return [];
        }

        return [.. found.Select(DependencyText)];
    }

    /// <summary>One dependency as a line: name, required version and the optional marker when the API sent them.</summary>
    private static string DependencyText(ForgeDependency dependency)
    {
        var name = dependency.Name ?? dependency.Guid ?? dependency.ModId?.ToString(CultureInfo.InvariantCulture) ?? "?";
        var version = dependency.Version is { } required ? " " + required.ToString() : string.Empty;
        var optional = dependency.IsOptional == true ? " (optional)" : string.Empty;
        return name + version + optional;
    }
}

/// <summary>A category in the filter box.</summary>
public sealed record ForgeCategoryOption(int Id, string Name);

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

    public string? Guid
    {
        get { return Mod.Guid; }
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

    public SemanticVersion LatestVersion { get; }
}

/// <summary>One version of the selected mod, with the SPT constraint it was published with.</summary>
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
