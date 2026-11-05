using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly IServerList _servers;
    private readonly IServerPortService _ports;
    private readonly IEndpointProbe _probe;
    private readonly ICertificateTrust _trust;
    private readonly IServerSyncService _sync;
    private readonly IInstanceContext _context;
    private readonly IDialogService _dialogs;
    private readonly ISptServerSupervisor _server;
    private readonly ILogger<ServerPageViewModel> _logger;

    public ServerPageViewModel(
        ILocalizer localizer,
        ISptServerSupervisor supervisor,
        IServerProfileService profiles,
        IServerList servers,
        IServerPortService ports,
        IEndpointProbe probe,
        ICertificateTrust trust,
        IServerSyncService sync,
        IInstanceContext context,
        IDialogService dialogs,
        ILogger<ServerPageViewModel> logger
    )
        : base(localizer)
    {
        _profiles = profiles;
        _servers = servers;
        _ports = ports;
        _probe = probe;
        _trust = trust;
        _dialogs = dialogs;
        _logger = logger;
        _server = supervisor;
        _sync = sync;
        _context = context;
        _server.StatusChanged += (_, status) => Apply(status.InstanceId, status);
        _context.Changed += (_, _) => _ = RefreshAsync(CancellationToken.None);
        _trust.Changed += (_, change) => OnTrustChanged(change);
    }

    public ObservableCollection<string> Output { get; } = [];

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    /// <summary>The servers the launcher keeps: the local one first, then whatever the user added.</summary>
    public ObservableCollection<ServerEntryRowViewModel> Servers { get; } = [];

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

    [ObservableProperty]
    public partial ServerEntryRowViewModel? SelectedServer { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditHost { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditPort { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerStatusText { get; set; } = string.Empty;

    public bool HasServers
    {
        get { return Servers.Count > 0; }
    }

    /// <summary>True when the active server runs on this machine, which is the only kind the launcher can start.</summary>
    public bool IsLocalActive
    {
        get { return _servers.Active.IsLocal; }
    }

    public string ActiveServerAddress
    {
        get { return _servers.Active.Address; }
    }

    /// <summary>Only a server on this machine can be started here.</summary>
    public bool CanStart
    {
        get { return IsLocalActive && !IsRunning; }
    }

    /// <summary>And only one that is running here can be stopped here.</summary>
    public bool CanStop
    {
        get { return IsLocalActive && IsRunning; }
    }

    public bool CanRemoveSelectedServer
    {
        get { return SelectedServer is { Entry.IsBuiltIn: false }; }
    }

    public string ServersLabel
    {
        get { return Text("server.servers"); }
    }

    public string AddServerLabel
    {
        get { return Text("server.addServer"); }
    }

    public string SaveServerLabel
    {
        get { return Text("common.save"); }
    }

    public string RemoveServerLabel
    {
        get { return Text("common.remove"); }
    }

    public string ActivateServerLabel
    {
        get { return Text("server.activate"); }
    }

    public string TestServerLabel
    {
        get { return Text("server.test"); }
    }

    public string ServerNameLabel
    {
        get { return Text("server.name"); }
    }

    public string ServerHostLabel
    {
        get { return Text("server.host"); }
    }

    public string ServerPortLabel
    {
        get { return Text("server.portLabel"); }
    }

    public string ActiveServerLabel
    {
        get { return Text("server.active"); }
    }

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
        await LoadServersAsync(cancellationToken);

        if (_context.Active is not { } instance)
        {
            Profiles.Clear();
            Output.Clear();
            StateText = Text("server.stateOff");
            IsRunning = false;
            OnPropertyChanged(nameof(HasProfiles));
            return;
        }

        // A server elsewhere is not a process this launcher watches: it is answering or it is not.
        if (RemoteEndpoint() is { } remote)
        {
            try
            {
                StateText = Localizer.Format("server.remoteState", remote.BaseUri);
                IsRunning = false;
                await LoadProfilesAsync(instance.Id, cancellationToken);
            }
            catch (ServerNotRunningException exception)
            {
                Profiles.Clear();
                OnPropertyChanged(nameof(HasProfiles));
                ServerStatusText = ViewModelErrors.Describe(exception, Localizer);
            }

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

    /// <summary>Reads the server list and fills the editor with the active entry.</summary>
    private async Task LoadServersAsync(CancellationToken cancellationToken)
    {
        await _servers.LoadAsync(cancellationToken);

        var active = _servers.Active;
        Servers.Clear();
        foreach (var entry in _servers.All)
        {
            Servers.Add(
                new ServerEntryRowViewModel(Localizer, entry, isActive: entry.Name.Equals(active.Name, StringComparison.OrdinalIgnoreCase))
            );
        }

        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(IsLocalActive));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(ActiveServerAddress));

        var keep = SelectedServer?.Entry.Name;
        SelectedServer =
            Servers.FirstOrDefault(row => string.Equals(row.Name, keep, StringComparison.OrdinalIgnoreCase))
            ?? Servers.FirstOrDefault(row => row.IsActive)
            ?? Servers.FirstOrDefault();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
    }

    partial void OnSelectedServerChanged(ServerEntryRowViewModel? value)
    {
        if (value is not { } row)
        {
            return;
        }

        EditName = row.Entry.Name;
        EditHost = row.Entry.Host;
        EditPort = row.Entry.Port.ToString(CultureInfo.InvariantCulture);
        ServerStatusText = string.Empty;
        OnPropertyChanged(nameof(CanRemoveSelectedServer));
    }

    /// <summary>Adding starts a row the editor can fill in; nothing is stored until it is saved.</summary>
    [RelayCommand]
    private void AddServer()
    {
        var taken = Servers.Select(row => row.Entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        while (taken.Contains($"Server {index}"))
        {
            index++;
        }

        var entry = new ServerEntry
        {
            Name = $"Server {index}",
            Host = "127.0.0.1",
            Port = ServerEndpoint.DefaultPort,
        };
        var row = new ServerEntryRowViewModel(Localizer, entry, isActive: false);
        Servers.Add(row);
        SelectedServer = row;
    }

    [RelayCommand]
    private async Task SaveServer(CancellationToken cancellationToken)
    {
        if (!int.TryParse(EditPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            ServerStatusText = Text("server.invalidPort");
            return;
        }

        var draft = new ServerEntry
        {
            Name = EditName.Trim(),
            Host = EditHost.Trim(),
            Port = port,
            IsBuiltIn = SelectedServer?.Entry.IsBuiltIn ?? false,
        };

        if (draft.ValidationError() is { } validation)
        {
            ServerStatusText = ViewModelErrors.Describe(new ServerListException(validation), Localizer);
            return;
        }

        try
        {
            var saved = await _servers.SaveAsync(draft, cancellationToken);
            var message = Text("server.saved");

            // The local entry's port is the server's own: http.json is what the server reads, so the edit goes there
            // too, and a running server keeps the old one until it is restarted.
            if (saved.IsLocal && _context.ActiveId is { } instanceId)
            {
                await _ports.SetPortAsync(instanceId, saved.Port, cancellationToken);
                message = Text("server.portApplied");
            }

            await LoadServersAsync(cancellationToken);

            // A renamed row is a new name in the list: the saved entry, not the old name, is the one to keep selected.
            SelectedServer =
                Servers.FirstOrDefault(row => string.Equals(row.Name, saved.Name, StringComparison.OrdinalIgnoreCase)) ?? SelectedServer;
            ServerStatusText = message;
        }
        catch (Exception exception) when (exception is ServerListException or ServerPortException)
        {
            ServerStatusText = ViewModelErrors.Describe(exception, Localizer);
        }
    }

    [RelayCommand]
    private async Task RemoveServer(CancellationToken cancellationToken)
    {
        if (SelectedServer is not { } row)
        {
            return;
        }

        try
        {
            await _servers.RemoveAsync(row.Entry.Name, cancellationToken);
            await LoadServersAsync(cancellationToken);
        }
        catch (ServerListException exception)
        {
            ServerStatusText = ViewModelErrors.Describe(exception, Localizer);
        }
    }

    [RelayCommand]
    private async Task ActivateServer(CancellationToken cancellationToken)
    {
        if (SelectedServer is not { } row)
        {
            return;
        }

        try
        {
            await _servers.ActivateAsync(row.Entry.Name, cancellationToken);
            ServerStatusText = Text("server.activated");
            await LoadServersAsync(cancellationToken);
        }
        catch (ServerListException exception)
        {
            ServerStatusText = ViewModelErrors.Describe(exception, Localizer);
        }
    }

    [RelayCommand]
    private async Task TestServer(CancellationToken cancellationToken)
    {
        if (SelectedServer is not { } row)
        {
            return;
        }

        ServerStatusText = Text("server.testing");
        var result = await _probe.ProbeAsync(row.Entry.Endpoint, cancellationToken);
        ServerStatusText = result.Reachable
            ? Localizer.Format("server.reachable", result.Milliseconds ?? 0)
            : Localizer.Format("server.unreachable", result.Failure ?? string.Empty);
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
            var profiles = await _profiles.CreateAsync(
                instanceId,
                NewUsername.Trim(),
                SelectedEdition,
                cancellationToken,
                RemoteEndpoint()
            );
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
            var summary = await _profiles.SelectAsync(instanceId, row.Username, cancellationToken, RemoteEndpoint());
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
            var result = await _profiles.WipeAsync(instanceId, row.Username, confirmed: true, cancellationToken, RemoteEndpoint());
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
            var result = await _profiles.DeleteAsync(instanceId, row.Username, confirmed: true, cancellationToken, RemoteEndpoint());
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
            Fill(await _profiles.ListAsync(instanceId, cancellationToken, RemoteEndpoint()));
        }
        catch (ServerNotRunningException exception)
        {
            _logger.LogDebug(exception, "Profiles asked while the server was not running");
            Profiles.Clear();
            OnPropertyChanged(nameof(HasProfiles));
        }
    }

    /// <summary>
    /// The active server, when it is not this machine: profile actions then happen over the network and the local
    /// supervisor is not involved. <c>null</c> means the instance's own server.
    /// </summary>
    /// <summary>
    /// A server presenting a different certificate is reported, never blocked (D25): SPT servers are self-signed and
    /// there is no authority to check against.
    /// </summary>
    private void OnTrustChanged(CertificateTrustChange change)
    {
        ServerStatusText = Localizer.Format("server.certificateChanged", change.Address);
    }

    private ServerEndpoint? RemoteEndpoint()
    {
        var active = _servers.Active;
        return active.IsLocal ? null : active.Endpoint;
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

/// <summary>One row of the server list.</summary>
public sealed class ServerEntryRowViewModel : ViewModelBase
{
    public ServerEntryRowViewModel(ILocalizer localizer, ServerEntry entry, bool isActive)
        : base(localizer)
    {
        IsActive = isActive;
        Entry = entry;
    }

    public ServerEntry Entry { get; }

    /// <summary>True for the entry the launcher is working with.</summary>
    public bool IsActive { get; }

    public string Name
    {
        get { return Entry.Name; }
    }

    public string Address
    {
        get { return Entry.Address; }
    }

    /// <summary>What the row says about itself: whether the launcher can run it, and whether it is the active one.</summary>
    public string DetailText
    {
        get
        {
            var kind = Entry.IsLocal ? Text("server.localKind") : Text("server.remoteKind");
            return IsActive ? Text("server.active") + " · " + kind : kind;
        }
    }
}
