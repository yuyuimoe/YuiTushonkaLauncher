using System;
using CommunityToolkit.Mvvm.ComponentModel;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Services;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>The bottom bar: what instance is open, the server state, the patch state and whether anything downloads.</summary>
public sealed class StatusBarViewModel : ViewModelBase
{
    private readonly IInstanceContext _context;
    private readonly ISptServerSupervisor _server;
    private readonly ISettingsService _settings;
    private readonly IDownloadActivity _downloads;

    public StatusBarViewModel(
        ILocalizer localizer,
        IInstanceContext context,
        ISptServerSupervisor server,
        ISettingsService settings,
        IDownloadActivity downloads
    )
        : base(localizer)
    {
        _context = context;
        _server = server;
        _settings = settings;
        _downloads = downloads;
        _context.Changed += (_, _) => Refresh();
        _server.StatusChanged += (_, _) => Refresh();
        _downloads.Changed += (_, _) => Refresh();
    }

    public string InstanceText
    {
        get { return _context.Active is { } instance ? Localizer.Format("status.instance", instance.Name) : Text("status.noInstance"); }
    }

    public string ServerText
    {
        get
        {
            var state = _context.Active is { } instance ? _server.GetStatus(instance.Id).State : ServerState.Stopped;
            var word = state switch
            {
                ServerState.Running => Text("launch.stepDetached"),
                ServerState.Starting => Text("launch.stepServer"),
                ServerState.Failed => Text("launch.failed"),
                _ => Text("server.stateOff"),
            };

            return Localizer.Format("status.server", word);
        }
    }

    public string PatchText
    {
        get
        {
            return _context.Active is { } instance
                ? Localizer.Format("status.patch", Text(instance.Patch.IsPatched ? "patch.patched" : "patch.clean"))
                : Localizer.Format("status.patch", Text("common.emptyValue"));
        }
    }

    public string DownloadsText
    {
        get { return Localizer.Format("status.downloads", _downloads.Running); }
    }

    /// <summary>Recomputes every label; a language switch or a status change calls this.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(InstanceText));
        OnPropertyChanged(nameof(ServerText));
        OnPropertyChanged(nameof(PatchText));
        OnPropertyChanged(nameof(DownloadsText));
    }
}

/// <summary>One entry of the left rail.</summary>
public partial class NavItemViewModel : ViewModelBase
{
    public NavItemViewModel(ILocalizer localizer, LauncherSection section, string glyph, string labelKey)
        : base(localizer)
    {
        Section = section;
        Glyph = glyph;
        LabelKey = labelKey;
    }

    public LauncherSection Section { get; }

    public string Glyph { get; }

    public string LabelKey { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Label
    {
        get { return Text(LabelKey); }
    }

    /// <summary>The accessible name of the rail button, which is what a screen reader says.</summary>
    public string AccessibleName
    {
        get { return Label; }
    }
}
