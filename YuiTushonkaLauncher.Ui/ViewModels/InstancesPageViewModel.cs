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
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The instance list: create, adopt, switch, delete. Opening a card makes it active and sends the user to the mods
/// page, which is what the left rail acts on.
/// </summary>
public partial class InstancesPageViewModel : PageViewModel
{
    private readonly IInstanceService _instances;
    private readonly IAdoptService _adopt;
    private readonly IInstallDetector _detector;
    private readonly IInstanceContext _context;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    public InstancesPageViewModel(
        ILocalizer localizer,
        IInstanceService instances,
        IAdoptService adopt,
        IInstallDetector detector,
        IInstanceContext context,
        IDialogService dialogs,
        INavigationService navigation
    )
        : base(localizer)
    {
        _instances = instances;
        _adopt = adopt;
        _detector = detector;
        _context = context;
        _dialogs = dialogs;
        _navigation = navigation;
        _context.Changed += (_, _) => Rebuild();
    }

    public ObservableCollection<InstanceCardViewModel> Instances { get; } = [];

    [ObservableProperty]
    public partial InstanceCardViewModel? Selected { get; set; }

    public string Title
    {
        get { return Text("instances.title"); }
    }

    public string EmptyText
    {
        get { return Text("instances.empty"); }
    }

    public string EmptyHint
    {
        get { return Text("instances.emptyHint"); }
    }

    public string CreateLabel
    {
        get { return Text("common.create"); }
    }

    public string AdoptLabel
    {
        get { return Text("common.adopt"); }
    }

    public string RefreshLabel
    {
        get { return Text("common.refresh"); }
    }

    public string DeleteLabel
    {
        get { return Text("common.delete"); }
    }

    public string OpenLabel
    {
        get { return Text("common.open"); }
    }

    public string SetActiveLabel
    {
        get { return Text("instances.setActive"); }
    }

    public bool HasInstances
    {
        get { return Instances.Count > 0; }
    }

    /// <summary>Loads the list; the shell calls this when the page is first shown.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await _context.RefreshAsync(cancellationToken);
            Rebuild();
        });
    }

    [RelayCommand]
    private async Task OpenAsync(InstanceCardViewModel? card, CancellationToken cancellationToken)
    {
        if (card is null)
        {
            return;
        }

        await _context.SetActiveAsync(card.Id, cancellationToken);
        Selected = Instances.FirstOrDefault(candidate => candidate.Id == card.Id);
        _navigation.Navigate(LauncherSection.Mods);
    }

    [RelayCommand]
    private async Task SetActiveAsync(InstanceCardViewModel? card, CancellationToken cancellationToken)
    {
        if (card is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _context.SetActiveAsync(card.Id, cancellationToken);
            Selected = Instances.FirstOrDefault(candidate => candidate.Id == card.Id);
        });
    }

    [RelayCommand]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        await CreateOrAdoptAsync(adopting: false, cancellationToken);
    }

    [RelayCommand]
    private async Task AdoptAsync(CancellationToken cancellationToken)
    {
        await CreateOrAdoptAsync(adopting: true, cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteAsync(InstanceCardViewModel? card, CancellationToken cancellationToken)
    {
        if (card is null)
        {
            return;
        }

        var question = new ConfirmDialogViewModel(Localizer)
        {
            Title = Text("instances.removeTitle"),
            Body = Text("instances.removeBody"),
            ConfirmLabel = Text("common.delete"),
            HasCheckbox = true,
            CheckboxLabel = Text("instances.deleteFiles"),
            CheckboxValue = false,
        };

        if (!await _dialogs.ConfirmAsync(question, cancellationToken))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _instances.DeleteAsync(card.Id, question.CheckboxValue, cancellationToken);
            await _context.RefreshAsync(cancellationToken);
            Status = Text("instances.deleteDone");
        });
    }

    private async Task CreateOrAdoptAsync(bool adopting, CancellationToken cancellationToken)
    {
        ClearMessages();
        var dialog = new CreateInstanceDialogViewModel(Localizer, _instances, _adopt, _detector, _dialogs, _context)
        {
            IsAdopting = adopting,
        };

        if (adopting)
        {
            dialog.BaseRoot = DefaultRoot();
        }
        else if (string.IsNullOrWhiteSpace(dialog.DestinationRoot))
        {
            dialog.DestinationRoot = DefaultRoot();
        }

        var instance = await _dialogs.CreateInstanceAsync(dialog, cancellationToken);
        if (instance is null)
        {
            Error = dialog.Error;
            return;
        }

        await RunAsync(async () =>
        {
            await _context.RefreshAsync(cancellationToken);
            Rebuild();
            Selected = Instances.FirstOrDefault(card => card.Id == instance.Id);
        });
    }

    private string DefaultRoot()
    {
        return _context.Known.Count > 0 ? System.IO.Path.GetDirectoryName(_context.Known[0].Path) ?? string.Empty : string.Empty;
    }

    /// <summary>Rebuilds the cards from the context, which owns the authoritative list.</summary>
    private void Rebuild()
    {
        var activeId = _context.ActiveId;
        var previous = Selected?.Id;
        Instances.Clear();
        foreach (var instance in _context.Known)
        {
            Instances.Add(new InstanceCardViewModel(Localizer, instance, instance.Id == activeId));
        }

        Selected = Instances.FirstOrDefault(card => card.Id == previous) ?? Instances.FirstOrDefault();
        OnPropertyChanged(nameof(HasInstances));
    }
}
