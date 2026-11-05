using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// "Which server?" before a launch. The local entry starts this instance's server with it; any other entry is a server
/// that runs elsewhere, and choosing it only changes the address the game is given.
/// </summary>
public partial class SelectServerDialogViewModel : ViewModelBase
{
    public SelectServerDialogViewModel(ILocalizer localizer, IReadOnlyList<ServerEntry> servers, string? preselected = null)
        : base(localizer)
    {
        foreach (var server in servers)
        {
            var row = new ServerChoiceViewModel(
                localizer,
                server,
                string.Equals(server.Name, preselected, StringComparison.OrdinalIgnoreCase)
            );
            Servers.Add(row);
        }

        Selected = Servers.FirstOrDefault(row => row.IsActive) ?? Servers.FirstOrDefault();
    }

    /// <summary>Raised by the window's own buttons; the window closes with the chosen entry or <c>null</c>.</summary>
    public event EventHandler<ServerEntry?>? Closed;

    /// <summary>The servers the launcher knows.</summary>
    public ObservableCollection<ServerChoiceViewModel> Servers { get; } = [];

    [ObservableProperty]
    public partial ServerChoiceViewModel? Selected { get; set; }

    /// <summary>Remember this choice for the instance, so the next launch does not ask again.</summary>
    [ObservableProperty]
    public partial bool RememberChoice { get; set; } = true;

    public string Title
    {
        get { return Text("server.pickTitle"); }
    }

    public string Body
    {
        get { return Text("server.pickBody"); }
    }

    public string RememberLabel
    {
        get { return Text("server.pickRemember"); }
    }

    public string LocalHint
    {
        get { return Text("server.pickLocalHint"); }
    }

    public string RemoteHint
    {
        get { return Text("server.pickRemoteHint"); }
    }

    public string CancelLabel
    {
        get { return Text("common.cancel"); }
    }

    public string ConfirmLabel
    {
        get { return Text("server.pickConfirm"); }
    }

    public bool CanConfirm
    {
        get { return Selected is not null; }
    }

    /// <summary>What the chosen server means, in one line under the list.</summary>
    public string SelectedHint
    {
        get
        {
            return Selected is { IsLocal: true } ? LocalHint
                : Selected is null ? string.Empty
                : RemoteHint;
        }
    }

    partial void OnSelectedChanged(ServerChoiceViewModel? value)
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(SelectedHint));
    }

    [RelayCommand]
    private void Accept()
    {
        Closed?.Invoke(this, Selected?.Entry);
    }

    [RelayCommand]
    private void Cancel()
    {
        Closed?.Invoke(this, null);
    }
}

/// <summary>One row of the picker.</summary>
public sealed class ServerChoiceViewModel : ViewModelBase
{
    public ServerChoiceViewModel(ILocalizer localizer, ServerEntry entry, bool isActive)
        : base(localizer)
    {
        Entry = entry;
        IsActive = isActive;
    }

    public ServerEntry Entry { get; }

    /// <summary>The one the launcher is currently set to.</summary>
    public bool IsActive { get; }

    public string Name
    {
        get { return Entry.Name; }
    }

    public string Address
    {
        get { return Entry.Address; }
    }

    public bool IsLocal
    {
        get { return Entry.IsLocal; }
    }

    /// <summary>What the row says about itself: whether the launcher can run it, and whether it is the current one.</summary>
    public string DetailText
    {
        get
        {
            var kind = Entry.IsLocal ? Text("server.localKind") : Text("server.remoteKind");
            return IsActive ? Text("server.active") + " · " + kind : kind;
        }
    }
}
