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
/// The create/adopt window: the user picks a base install and a destination, sees what would be linked, copied or
/// skipped, and only then creates it. Adopting an existing merged install is the same window with a different title.
/// </summary>
public partial class CreateInstanceDialogViewModel : ViewModelBase
{
    private readonly IInstanceService _instances;
    private readonly IDialogService _dialogs;
    private readonly IInstanceContext _context;

    public CreateInstanceDialogViewModel(ILocalizer localizer, IInstanceService instances, IDialogService dialogs, IInstanceContext context)
        : base(localizer)
    {
        _instances = instances;
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

    [ObservableProperty]
    public partial bool IsAdopting { get; set; }

    [ObservableProperty]
    public partial InstancePlan? Plan { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

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
        get { return Text("instances.source"); }
    }

    public string DestinationLabel
    {
        get { return Text("instances.destination"); }
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

    public string ModsLabel
    {
        get { return Text("instances.mods"); }
    }

    partial void OnIsAdoptingChanged(bool value)
    {
        RefreshLocalization();
    }

    partial void OnSourcePathChanged(string value)
    {
        Plan = null;
        Summary = string.Empty;
    }

    [RelayCommand]
    private async Task BrowseSourceAsync(CancellationToken cancellationToken)
    {
        var folder = await _dialogs.PickFolderAsync(SourceLabel, startAt: SourcePath, cancellationToken);
        if (folder is not null)
        {
            SourcePath = folder;
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
            Plan = await _instances.PlanAsync(Request(), cancellationToken);
            Summary = FormatPlan(Plan);
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
            var instance = await _instances.CreateAsync(Request(), cancellationToken: cancellationToken);
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

    private InstanceCreationRequest Request()
    {
        return new InstanceCreationRequest
        {
            Name = Name.Trim(),
            Location = string.IsNullOrWhiteSpace(DestinationRoot) ? DefaultRoot() : DestinationRoot.Trim(),
            BaseInstallPath = SourcePath.Trim(),
        };
    }

    private string DefaultRoot()
    {
        return _context.Known.FirstOrDefault() is { } existing
            ? System.IO.Path.GetDirectoryName(existing.Path) ?? string.Empty
            : string.Empty;
    }
}
