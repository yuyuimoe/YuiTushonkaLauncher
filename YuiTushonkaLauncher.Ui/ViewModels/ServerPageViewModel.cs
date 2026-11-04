using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Core.Formatting;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The server tab: its state and output, the profiles the server owns, and the mod-list sync. Everything here needs a
/// running server; the page says so instead of failing with an exception when it is not.
/// </summary>
public partial class ServerPageViewModel : PageViewModel
{
    /// <summary>The editions the server accepts, in the order the game shows them.</summary>
    public static readonly IReadOnlyList<string> Editions =
    [
        "Standard",
        "Left Behind",
        "Prepare To Escape",
        "Edge Of Darkness",
        "Unheard",
        "SPT Developer",
        "SPT Easy start",
        "SPT Zero to hero",
    ];

    private readonly IServerProfileService _profiles;
    private readonly IServerSyncService _sync;
    private readonly IInstanceContext _context;
    private readonly IDialogService _dialogs;
    private readonly ISptServerSupervisor _server;
    private readonly ILogger<ServerPageViewModel> _logger;

    public ServerPageViewModel(
        ILocalizer localizer,
        ISptServerSupervisor supervisor,
        IServerProfileService profiles,
        IServerSyncService sync,
        IInstanceContext context,
        IDialogService dialogs,
        ILogger<ServerPageViewModel> logger
    )
        : base(localizer)
    {
        _profiles = profiles;
        _dialogs = dialogs;
        _logger = logger;
        _server = supervisor;
        _sync = sync;
        _context = context;
        _server.StatusChanged += (_, status) => Apply(status.InstanceId, status);
        _context.Changed += (_, _) => _ = RefreshAsync(CancellationToken.None);
    }

    public ObservableCollection<string> Output { get; } = [];

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    [ObservableProperty]
    public partial string StatusLine { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PortText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string NewUsername { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedEdition { get; set; } = Editions[0];

    [ObservableProperty]
    public partial ProfileRowViewModel? SelectedProfile { get; set; }

    public string Title
    {
        get { return Text("server.title"); }
    }

    public string StartLabel
    {
        get { return Text("server.start"); }
    }

    public string StopLabel
    {
        get { return Text("server.stop"); }
    }

    public string OutputLabel
    {
        get { return Text("server.output"); }
    }

    public string ProfilesLabel
    {
        get { return Text("server.profiles"); }
    }

    public string ProfilesEmptyText
    {
        get { return Text("server.profilesEmpty"); }
    }

    public string NewProfileLabel
    {
        get { return Text("server.newProfile"); }
    }

    public string UsernameLabel
    {
        get { return Text("server.username"); }
    }

    public string EditionLabel
    {
        get { return Text("server.edition"); }
    }

    public string SelectLabel
    {
        get { return Text("server.select"); }
    }

    public string WipeLabel
    {
        get { return Text("server.wipe"); }
    }

    public string DeleteLabel
    {
        get { return Text("common.delete"); }
    }

    public string SyncLabel
    {
        get { return Text("server.syncNow"); }
    }

    public bool HasProfiles
    {
        get { return Profiles.Count > 0; }
    }

    public bool CanCreateProfile
    {
        get { return IsRunning && !IsBusy && NewUsername.Trim().Length > 0; }
    }

    public bool CanActOnProfile
    {
        get { return IsRunning && !IsBusy && SelectedProfile is not null; }
    }

    /// <summary>Reads the supervisor's state and, when it runs, the profiles and the sync plan.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_context.Active is not { } instance)
        {
            Profiles.Clear();
            Output.Clear();
            StateText = Text("server.stateOff");
            IsRunning = false;
            OnPropertyChanged(nameof(HasProfiles));
            return;
        }

        var status = _server.GetStatus(instance.Id);
        Apply(instance.Id, status);

        if (status.State is not (ServerState.Running or ServerState.Starting))
        {
            Profiles.Clear();
            OnPropertyChanged(nameof(HasProfiles));
            return;
        }

        await LoadProfilesAsync(instance.Id, cancellationToken);
    }

    partial void OnNewUsernameChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreateProfile));
    }

    partial void OnSelectedProfileChanged(ProfileRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanActOnProfile));
    }

    protected override void OnBusyChanged(bool isBusy)
    {
        OnPropertyChanged(nameof(CanCreateProfile));
        OnPropertyChanged(nameof(CanActOnProfile));
    }

    [RelayCommand]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_context.Active is not { } instance)
        {
            Error = Text("errors.noInstance");
            return;
        }

        await RunAsync(async () =>
        {
            var status = await _server.StartAsync(instance, cancellationToken: cancellationToken);
            Apply(instance.Id, status);
            if (status.State == ServerState.Running)
            {
                Status = Localizer.Format("server.started", status.ReadyAfter is { } after ? Bytes.Format((long)after.TotalSeconds) : "-");
                await LoadProfilesAsync(instance.Id, cancellationToken);
            }
            else if (status.FailureReason is { } reason)
            {
                Error = reason;
            }
        });
    }

    [RelayCommand]
    private async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_context.Active is not { } instance)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var status = await _server.StopAsync(instance.Id, cancellationToken: cancellationToken);
            Apply(instance.Id, status);
            Status = Text("server.stopped");
            Profiles.Clear();
            OnPropertyChanged(nameof(HasProfiles));
        });
    }

    [RelayCommand]
    private async Task CreateProfileAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId || !CanCreateProfile)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var profiles = await _profiles.CreateAsync(instanceId, NewUsername.Trim(), SelectedEdition, cancellationToken);
            Fill(profiles);
            NewUsername = string.Empty;
        });
    }

    [RelayCommand]
    private async Task SelectProfileAsync(ProfileRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null || _context.ActiveId is not { } instanceId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var summary = await _profiles.SelectAsync(instanceId, row.Username, cancellationToken);
            Status = Localizer.Format("server.selected", summary.Username);
            await LoadProfilesAsync(instanceId, cancellationToken);
        });
    }

    [RelayCommand]
    private async Task WipeAsync(ProfileRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null || _context.ActiveId is not { } instanceId)
        {
            return;
        }

        var question = new ConfirmDialogViewModel(Localizer)
        {
            Title = Text("server.wipeTitle"),
            Body = Text("server.wipeBody"),
            ConfirmLabel = Text("server.wipe"),
        };

        if (!await _dialogs.ConfirmAsync(question, cancellationToken))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _profiles.WipeAsync(instanceId, row.Username, confirmed: true, cancellationToken);
            Fill(result.Profiles);
            Status = result.BackupPath is { } backup ? Localizer.Format("patch.backup", backup) : string.Empty;
        });
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(ProfileRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null || _context.ActiveId is not { } instanceId)
        {
            return;
        }

        var question = new ConfirmDialogViewModel(Localizer)
        {
            Title = Text("server.deleteTitle"),
            Body = Text("server.deleteBody"),
            ConfirmLabel = Text("common.delete"),
        };

        if (!await _dialogs.ConfirmAsync(question, cancellationToken))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _profiles.DeleteAsync(instanceId, row.Username, confirmed: true, cancellationToken);
            Fill(result.Profiles);
        });
    }

    [RelayCommand]
    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var plan = await _sync.PlanAsync(instanceId, cancellationToken: cancellationToken);
            SyncText = plan.IsInSync ? Text("server.inSync") : Localizer.Format("server.outOfSync", plan.Actionable.Count);
            if (plan.IsInSync || plan.Actionable.Count == 0)
            {
                return;
            }

            var question = new ConfirmDialogViewModel(Localizer)
            {
                Title = Text("server.sync"),
                Body = SyncText,
                ConfirmLabel = Text("server.syncNow"),
            };

            if (!await _dialogs.ConfirmAsync(question, cancellationToken))
            {
                return;
            }

            var result = await _sync.ApplyAsync(instanceId, plan, cancellationToken: cancellationToken);
            SyncText = result.Succeeded
                ? Text("server.inSync")
                : Localizer.Format("server.outOfSync", result.Outcomes.Count(outcome => !outcome.Applied));
        });
    }

    private void Fill(IReadOnlyList<SptProfileSummary> profiles)
    {
        var selected = SelectedProfile?.Username;
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(new ProfileRowViewModel(Localizer, profile));
        }

        SelectedProfile = Profiles.FirstOrDefault(row => row.Username == selected) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasProfiles));
    }

    private async Task LoadProfilesAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        try
        {
            Fill(await _profiles.ListAsync(instanceId, cancellationToken));
        }
        catch (ServerNotRunningException exception)
        {
            _logger.LogDebug(exception, "Profiles asked while the server was not running");
            Profiles.Clear();
            OnPropertyChanged(nameof(HasProfiles));
        }
    }

    /// <summary>Applies a status the supervisor reported, for this instance only.</summary>
    private void Apply(Guid? instanceId, ServerStatus status)
    {
        if (instanceId is { } id && status.InstanceId != id)
        {
            return;
        }

        IsRunning = status.State is ServerState.Running or ServerState.Starting;
        StateText = status.State switch
        {
            ServerState.Running => Text("launch.stepDetached"),
            ServerState.Starting => Text("launch.stepServer"),
            ServerState.Failed => status.FailureReason ?? Text("launch.failed"),
            _ => Text("server.stateOff"),
        };
        PortText = status.Endpoint is { } endpoint ? Localizer.Format("server.port", endpoint.Port) : string.Empty;
        if (status.State is not (ServerState.Running or ServerState.Starting))
        {
            Profiles.Clear();
            OnPropertyChanged(nameof(HasProfiles));
        }

        Output.Clear();
        foreach (var line in _server.RecentOutput(status.InstanceId, 200).TakeLast(200))
        {
            Output.Add(line.Text);
        }
    }
}

/// <summary>One server profile row.</summary>
public sealed class ProfileRowViewModel : ViewModelBase
{
    public ProfileRowViewModel(ILocalizer localizer, SptProfileSummary profile)
        : base(localizer)
    {
        Profile = profile;
    }

    public SptProfileSummary Profile { get; }

    public string Username
    {
        get { return Profile.Username; }
    }

    public string Nickname
    {
        get { return Profile.Nickname.Length > 0 ? Profile.Nickname : Profile.Username; }
    }

    public int Level
    {
        get { return Profile.Level; }
    }

    public string Side
    {
        get { return Profile.Side; }
    }

    public string Edition
    {
        get { return Profile.Edition; }
    }

    public string StateText
    {
        get
        {
            return Profile.PendingWipe ? Text("server.wipe")
                : Profile.InvalidOrUnloadable ? Text("common.unknown")
                : string.Empty;
        }
    }
}
