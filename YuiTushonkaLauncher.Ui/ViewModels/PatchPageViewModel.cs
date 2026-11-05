using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Formatting;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The patch tab: what the deltas would do, the button that does it, and the way back. The plan is shown before the
/// button is enabled, because patching rewrites the game binary.
/// </summary>
public partial class PatchPageViewModel : PageViewModel
{
    private readonly IPatchService _patcher;
    private readonly IInstanceContext _context;

    public PatchPageViewModel(ILocalizer localizer, IPatchService patches, IInstanceContext context)
        : base(localizer)
    {
        _patcher = patches;
        _context = context;
        _context.Changed += (_, _) => _ = PlanAsync(CancellationToken.None);
    }

    public ObservableCollection<PatchFileRowViewModel> PatchFiles { get; } = [];

    [ObservableProperty]
    public partial string StateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BackupText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanApply { get; set; }

    [ObservableProperty]
    public partial bool CanRestore { get; set; }

    public string Title
    {
        get { return Text("instances.detail.patch"); }
    }

    public string ApplyLabel
    {
        get { return Text("patch.apply"); }
    }

    public string RestoreLabel
    {
        get { return Text("patch.restore"); }
    }

    public string RefreshLabel
    {
        get { return Text("common.refresh"); }
    }

    public string FilesLabel
    {
        get { return Text("mods.name"); }
    }

    public string ActionLabel
    {
        get { return Text("mods.kind"); }
    }

    public string SizeLabel
    {
        get { return Text("mods.version"); }
    }

    /// <summary>Asks the service what patching would do; touches nothing.</summary>
    [RelayCommand]
    public async Task PlanAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            PatchFiles.Clear();
            StateText = Text("errors.noInstance");
            CanApply = false;
            CanRestore = false;
            return;
        }

        await RunAsync(async () =>
        {
            var plan = await _patcher.PlanAsync(instanceId, cancellationToken);
            PatchFiles.Clear();
            foreach (var file in plan.Files)
            {
                PatchFiles.Add(new PatchFileRowViewModel(Localizer, file));
            }

            CanApply = plan.CanApply;
            CanRestore = await CanRestoreAsync(instanceId, cancellationToken);
            StateText =
                plan.IsFullyPatched ? Text("patch.patched")
                : CanApply ? Text("patch.clean")
                : Text("patch.unknown");
            BackupText = plan.Files.FirstOrDefault(file => file.BackupPath.Length > 0) is { } first
                ? Localizer.Format("patch.backup", first.BackupPath)
                : Text("patch.backupMissing");
        });
    }

    [RelayCommand]
    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            ProgressText = Text("patch.applying");
            var progress = new Progress<PatchProgress>(step => ProgressText = $"{step.FileName} {step.Percentage:F0}%");
            var report = await _patcher.PatchAsync(instanceId, progress, cancellationToken);
            ProgressText = string.Empty;
            StateText = report.Succeeded ? Text("patch.patched") : Text("patch.unknown");
            Status = report.Summary;
            await PlanAsync(cancellationToken);
        });
    }

    [RelayCommand]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            ProgressText = Text("patch.restoring");
            var report = await _patcher.RestoreAsync(instanceId, cancellationToken: cancellationToken);
            ProgressText = string.Empty;
            Status = report.Summary;
            await PlanAsync(cancellationToken);
        });
    }

    private async Task<bool> CanRestoreAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var plan = await _patcher.PlanAsync(instanceId, cancellationToken);
        return plan.Files.Any(file => file.Action == PatchFileAction.AlreadyPatched || file.BackupPath.Length > 0);
    }
}

/// <summary>One delta and what the plan will do with it.</summary>
public sealed class PatchFileRowViewModel : ViewModelBase
{
    public PatchFileRowViewModel(ILocalizer localizer, PatchFilePlan plan)
        : base(localizer)
    {
        Plan = plan;
    }

    public PatchFilePlan Plan { get; }

    public string Name
    {
        get { return Plan.RelativePath; }
    }

    public string ActionText
    {
        get { return Plan.Action.ToString(); }
    }

    public string SizeText
    {
        get { return Bytes.Format(Plan.ExpectedInputSize) + " → " + Bytes.Format(Plan.ExpectedOutputSize); }
    }

    public string Reason
    {
        get { return Plan.Reason; }
    }
}
