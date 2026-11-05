using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Formatting;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The window that adds an instance. Creating builds one from an install the SPT installer produced, in a folder of its
/// own; adopting takes an install that already runs and brings it in where it is. Both show what was detected — which
/// SPT release, which game build that release requires and which build is really there — before anything is written.
/// </summary>
public partial class CreateInstanceDialogViewModel : ViewModelBase
{
    private readonly IInstanceService _instances;
    private readonly IAdoptService _adopt;
    private readonly IInstallDetector _detector;
    private readonly IDialogService _dialogs;
    private readonly IInstanceContext _context;

    public CreateInstanceDialogViewModel(
        ILocalizer localizer,
        IInstanceService instances,
        IAdoptService adopt,
        IInstallDetector detector,
        IDialogService dialogs,
        IInstanceContext context
    )
        : base(localizer)
    {
        _instances = instances;
        _adopt = adopt;
        _detector = detector;
        _dialogs = dialogs;
        _context = context;
    }

    public event EventHandler<Core.Domain.Instance?>? Closed;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourcePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DestinationRoot { get; set; } = string.Empty;

    /// <summary>Pristine install used to tell game files from modifications; adopt only.</summary>
    [ObservableProperty]
    public partial string ReferencePath { get; set; } = string.Empty;

    /// <summary>Where a base install may be created when adopting without a reference; adopt only.</summary>
    [ObservableProperty]
    public partial string BaseRoot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAdopting { get; set; }

    [ObservableProperty]
    public partial InstallFingerprint? Fingerprint { get; set; }

    [ObservableProperty]
    public partial InstancePlan? Plan { get; set; }

    [ObservableProperty]
    public partial AdoptPlan? AdoptPlan { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetectionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The instance that was created, for a caller that drives the dialog without a window.</summary>
    [ObservableProperty]
    public partial Core.Domain.Instance? Instance { get; set; }

    public string Title
    {
        get { return Text(IsAdopting ? "instances.adoptTitle" : "instances.createTitle"); }
    }

    public string NameHint
    {
        get { return Text("instances.nameHint"); }
    }

    public string SourceLabel
    {
        get { return Text(IsAdopting ? "instances.adoptSource" : "instances.source"); }
    }

    public string DestinationLabel
    {
        get { return Text("instances.destination"); }
    }

    public string ReferenceLabel
    {
        get { return Text("instances.reference"); }
    }

    public string BaseRootLabel
    {
        get { return Text("instances.baseRoot"); }
    }

    public string CreateLabel
    {
        get { return Text(IsAdopting ? "common.adopt" : "common.create"); }
    }

    public string CancelLabel
    {
        get { return Text("common.cancel"); }
    }

    public string BrowseLabel
    {
        get { return Text("common.browse"); }
    }

    public string PlanLabel
    {
        get { return Text("common.plan"); }
    }

    public string DetectLabel
    {
        get { return Text("instances.detect"); }
    }

    /// <summary>Creating needs a destination folder; adopting keeps the install where it is.</summary>
    public bool ShowsDestination
    {
        get { return !IsAdopting; }
    }

    public bool ShowsAdoptOptions
    {
        get { return IsAdopting; }
    }

    /// <summary>Red when the game build the install holds is not the one SPT requires.</summary>
    public bool HasVersionMismatch
    {
        get { return Fingerprint is { IsInstall: true, TarkovMatches: false }; }
    }

    public bool IsDetectionReady
    {
        get { return Fingerprint is { IsInstall: true }; }
    }

    partial void OnIsAdoptingChanged(bool value)
    {
        RefreshLocalization();
        OnPropertyChanged(nameof(ShowsDestination));
        OnPropertyChanged(nameof(ShowsAdoptOptions));
    }

    partial void OnSourcePathChanged(string value)
    {
        Plan = null;
        AdoptPlan = null;
        Summary = string.Empty;
    }

    [RelayCommand]
    private async Task BrowseSourceAsync(CancellationToken cancellationToken)
    {
        var folder = await _dialogs.PickFolderAsync(SourceLabel, startAt: SourcePath, cancellationToken);
        if (folder is not null)
        {
            SourcePath = folder;
            await DetectAsync(cancellationToken);
        }
    }

    [RelayCommand]
    private async Task BrowseDestinationAsync(CancellationToken cancellationToken)
    {
        var folder = await _dialogs.PickFolderAsync(DestinationLabel, startAt: DestinationRoot, cancellationToken);
        if (folder is not null)
        {
            DestinationRoot = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseReferenceAsync(CancellationToken cancellationToken)
    {
        var folder = await _dialogs.PickFolderAsync(ReferenceLabel, startAt: ReferencePath, cancellationToken);
        if (folder is not null)
        {
            ReferencePath = folder;
            Plan = null;
            AdoptPlan = null;
        }
    }

    [RelayCommand]
    private async Task BrowseBaseRootAsync(CancellationToken cancellationToken)
    {
        var folder = await _dialogs.PickFolderAsync(BaseRootLabel, startAt: BaseRoot, cancellationToken);
        if (folder is not null)
        {
            BaseRoot = folder;
        }
    }

    /// <summary>Reads the versions out of the chosen folder, which is what makes this dialog version-aware.</summary>
    [RelayCommand]
    public async Task DetectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            return;
        }

        Error = string.Empty;
        IsBusy = true;
        try
        {
            var fingerprint = await Task.Run(() => _detector.Detect(SourcePath), cancellationToken);
            Fingerprint = fingerprint;
            DetectionText = fingerprint.Describe();
            OnPropertyChanged(nameof(HasVersionMismatch));
            OnPropertyChanged(nameof(IsDetectionReady));

            if (string.IsNullOrWhiteSpace(Name) && fingerprint.IsInstall)
            {
                Name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(fingerprint.RootPath));
            }
        }
        catch (Exception exception)
        {
            Error = ViewModelErrors.Describe(exception, Localizer);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PlanAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        Error = string.Empty;
        IsBusy = true;
        try
        {
            if (IsAdopting)
            {
                AdoptPlan = await _adopt.PlanAsync(AdoptRequest(), cancellationToken);
                Error = string.Join(Environment.NewLine, AdoptPlan.Issues);
                Summary = FormatAdoptPlan(AdoptPlan);
            }
            else
            {
                Plan = await _instances.PlanAsync(Request(), cancellationToken);
                Summary = FormatPlan(Plan);
            }
        }
        catch (Exception exception)
        {
            Error = ViewModelErrors.Describe(exception, Localizer);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        Error = string.Empty;
        IsBusy = true;
        try
        {
            if (Fingerprint is { IsInstall: true, TarkovMatches: false })
            {
                Error = Localizer.Format("instances.versionMismatch", Fingerprint.RequiredTarkovVersion, Fingerprint.ActualTarkovVersion);
                return;
            }

            var instance = IsAdopting
                ? await _adopt.AdoptAsync(AdoptRequest(), cancellationToken: cancellationToken)
                : await _instances.CreateAsync(Request(), cancellationToken: cancellationToken);

            Instance = instance;
            await _context.RefreshAsync(cancellationToken);
            await _context.SetActiveAsync(instance.Id, cancellationToken);
            Closed?.Invoke(this, instance);
        }
        catch (Exception exception)
        {
            Error = ViewModelErrors.Describe(exception, Localizer);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Closed?.Invoke(this, null);
    }

    /// <summary>The plan line: what the creation costs in disk, how much it shares and where it lands.</summary>
    private string FormatPlan(InstancePlan plan)
    {
        var lines = new List<string>
        {
            Localizer.Format("plan.link", plan.LinkCount),
            Localizer.Format("plan.copy", plan.CopyCount, Bytes.Format(plan.CopyBytes)),
        };

        if (plan.SkipCount > 0)
        {
            lines.Add(Localizer.Format("plan.skip", plan.SkipCount));
        }

        lines.Add(Localizer.Format("plan.destination", plan.InstancePath));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The adopt line: what stays, what is shared with the base, and what was recognised as a modification.</summary>
    private string FormatAdoptPlan(AdoptPlan plan)
    {
        var lines = new List<string>
        {
            Localizer.Format("adopt.shared", plan.SharedFiles, Bytes.Format(plan.SharedBytes)),
            Localizer.Format("adopt.modified", plan.ModifiedFiles + plan.ExtraFiles, Bytes.Format(plan.ModifiedBytes)),
        };

        if (plan.PatchedFiles > 0)
        {
            lines.Add(Localizer.Format("adopt.patched", plan.PatchedFiles));
        }

        if (plan.StateFiles > 0)
        {
            lines.Add(Localizer.Format("adopt.state", plan.StateFiles));
        }

        if (plan.BasePath is { } basePath)
        {
            lines.Add(Localizer.Format(plan.CreatesBase ? "adopt.newBase" : "adopt.existingBase", basePath));
        }

        if (plan.Modifications.Count > 0)
        {
            lines.Add(Localizer["adopt.kept"]);
            lines.AddRange(System.Linq.Enumerable.Take(plan.Modifications, 10));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private InstanceCreationRequest Request()
    {
        return new InstanceCreationRequest
        {
            Name = Name.Trim(),
            Location = string.IsNullOrWhiteSpace(DestinationRoot) ? DefaultRoot() : DestinationRoot.Trim(),
            BaseInstallPath = SourcePath.Trim(),
        };
    }

    private AdoptRequest AdoptRequest()
    {
        return new AdoptRequest
        {
            InstallPath = SourcePath.Trim(),
            ReferenceBasePath = string.IsNullOrWhiteSpace(ReferencePath) ? null : ReferencePath.Trim(),
            BaseRoot = string.IsNullOrWhiteSpace(BaseRoot) ? DefaultBaseRoot() : BaseRoot.Trim(),
            Name = Name.Trim(),
        };
    }

    private string DefaultRoot()
    {
        return _context.Known.FirstOrDefault() is { } existing
            ? System.IO.Path.GetDirectoryName(existing.Path) ?? string.Empty
            : string.Empty;
    }

    private string DefaultBaseRoot()
    {
        return _context.Known.FirstOrDefault() is { } existing
            ? System.IO.Path.GetDirectoryName(existing.Path) ?? string.Empty
            : string.Empty;
    }
}
