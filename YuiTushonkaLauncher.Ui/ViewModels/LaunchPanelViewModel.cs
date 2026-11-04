using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The launch button and the few choices around it. The state machine (<see cref="Session" />) decides what the button
/// says; this class only forwards commands and applies the "when the game starts" setting once the pair is detached.
/// </summary>
public partial class LaunchPanelViewModel : ViewModelBase
{
    private readonly ILaunchService _launch;
    private readonly IInstanceContext _context;
    private readonly ISettingsService _settings;
    private readonly IServerProfileService _profiles;
    private readonly IRunAdoptionService _runs;
    private readonly IWindowController _window;
    private readonly ILogger<LaunchPanelViewModel> _logger;
    private CancellationTokenSource? _running;

    public LaunchPanelViewModel(
        ILocalizer localizer,
        ILaunchService launch,
        IInstanceContext context,
        ISettingsService settings,
        IServerProfileService profiles,
        IRunAdoptionService runs,
        IWindowController window,
        ILogger<LaunchPanelViewModel> logger
    )
        : base(localizer)
    {
        _launch = launch;
        _context = context;
        _settings = settings;
        _profiles = profiles;
        _runs = runs;
        _window = window;
        _logger = logger;
        Session = new LaunchSession();
        Session.Changed += (_, _) => OnSessionChanged();
    }

    /// <summary>The state machine the button renders.</summary>
    public LaunchSession Session { get; }

    /// <summary>True while the pair is running or on its way somewhere, which is what the close question asks.</summary>
    public bool HasRunningProcesses()
    {
        return Session.State is LaunchButtonState.Running or LaunchButtonState.Stopping;
    }

    [ObservableProperty]
    public partial string ProfileUsername { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RememberProfile { get; set; } = true;

    [ObservableProperty]
    public partial bool SkipPatch { get; set; }

    /// <summary>Pre-flight refusals, each already phrased for a person.</summary>
    [ObservableProperty]
    public partial string ProblemsText { get; set; } = string.Empty;

    public bool HasProblems
    {
        get { return !string.IsNullOrEmpty(ProblemsText); }
    }

    public bool HasInstance
    {
        get { return _context.Active is not null; }
    }

    public bool CanPressLaunch
    {
        get { return Session.CanLaunch && HasInstance; }
    }

    public bool CanPressStop
    {
        get { return Session.CanStop; }
    }

    public bool CanPressCancel
    {
        get { return Session.CanCancel; }
    }

    public string LaunchLabel
    {
        get { return Text("launch.launch"); }
    }

    public string StopLabel
    {
        get { return Text("launch.stop"); }
    }

    public string CancelLabel
    {
        get { return Text("launch.cancel"); }
    }

    public string ProfileLabel
    {
        get { return Text("launch.profile"); }
    }

    public string RememberLabel
    {
        get { return Text("launch.rememberProfile"); }
    }

    public string SkipPatchLabel
    {
        get { return Text("launch.skipPatch"); }
    }

    /// <summary>The line under the button: a step description, a failure, or the final state.</summary>
    public string Message
    {
        get
        {
            return Session.State switch
            {
                LaunchButtonState.Running => Text("launch.running"),
                LaunchButtonState.Stopping => Text("launch.stopped"),
                LaunchButtonState.Failed when Session.Message.Length > 0 => Session.Message,
                LaunchButtonState.Cancelled => Session.Message.Length > 0 ? Session.Message : Text("launch.refused"),
                _ when Session.Message.Length > 0 => Session.Message,
                _ => Text("launch.stepPreFlight"),
            };
        }
    }

    /// <summary>Fills the profile box from what the launcher remembered for this instance.</summary>
    public async Task SyncProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Active is not { } instance)
        {
            ProfileUsername = string.Empty;
            return;
        }

        try
        {
            ProfileUsername = await _profiles.GetRememberedUsernameAsync(instance.Id, cancellationToken) ?? ProfileUsername;
        }
        catch (Exception exception)
        {
            // Remembering a name is a convenience; not being able to read it must not block launching.
            _logger.LogDebug(exception, "Could not read the remembered profile of {Instance}", instance.Name);
        }
    }

    [RelayCommand]
    private async Task LaunchAsync(CancellationToken cancellationToken)
    {
        if (_context.Active is not { } instance || !Session.CanLaunch)
        {
            return;
        }

        ProblemsText = string.Empty;
        _running?.Dispose();
        _running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = new Progress<LaunchProgress>(step => Session.Apply(step));

        try
        {
            var result = await _launch.LaunchAsync(
                new LaunchRequest
                {
                    InstanceId = instance.Id,
                    ProfileUsername = string.IsNullOrWhiteSpace(ProfileUsername) ? null : ProfileUsername.Trim(),
                    RememberProfile = RememberProfile,
                    SkipPatch = SkipPatch,
                },
                progress,
                _running.Token
            );

            ProblemsText = string.Join(Environment.NewLine, result.Problems.Select(problem => problem.Message));
            OnPropertyChanged(nameof(HasProblems));
            if (result.Problems.Count > 0 || !result.Succeeded)
            {
                Session.Fail(string.Join(" ", result.Problems.Select(problem => problem.Message)));
            }
            else
            {
                // The result is authoritative: a progress report can still be in flight (Progress<T> posts), and the
                // button must not stay on "starting" after the pipeline has said the pair is up.
                Session.Apply(new LaunchProgress(LaunchStep.Detached, LaunchStepOutcome.Succeeded, Text("launch.running")));
                _window.Apply(_settings.Current.GameStartWindow);
            }
        }
        catch (OperationCanceledException)
        {
            Session.Apply(new LaunchProgress(LaunchStep.PreFlight, LaunchStepOutcome.Cancelled, Text("launch.refused")));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Launch of {Instance} failed", instance.Name);
            Session.Fail(ViewModelErrors.Describe(exception, Localizer));
        }
        finally
        {
            OnSessionChanged();
        }
    }

    [RelayCommand]
    private void CancelLaunch()
    {
        if (Session.CanCancel)
        {
            _running?.Cancel();
        }
    }

    /// <summary>Stops the recorded pair. The record is the only thing that knows both pids, so it is the way back.</summary>
    [RelayCommand]
    private async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_context.Active is not { } instance)
        {
            return;
        }

        Session.BeginStop();
        try
        {
            foreach (var run in await _runs.ListAsync(cancellationToken))
            {
                if (run.Run.InstanceId == instance.Id)
                {
                    await _runs.StopAsync(run, cancellationToken: cancellationToken);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Stopping the processes of {Instance} failed", instance.Name);
            Session.Fail(ViewModelErrors.Describe(exception, Localizer));
            return;
        }

        Session.Stopped();
    }

    /// <summary>Called when the pair may have changed underneath (a server start or stop, a language switch).</summary>
    public void Refresh()
    {
        OnSessionChanged();
        OnPropertyChanged(nameof(HasInstance));
    }

    private void OnSessionChanged()
    {
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(CanPressLaunch));
        OnPropertyChanged(nameof(CanPressStop));
        OnPropertyChanged(nameof(CanPressCancel));
    }
}

/// <summary>What the launch panel may do to its window once the game is detached.</summary>
public interface IWindowController
{
    void Apply(GameStartWindowAction action);

    /// <summary>Shows the instance manager window on a section, creating it the first time.</summary>
    void OpenManager(Navigation.LauncherSection section);

    /// <summary>Shows the Forge window, browsing mods or addons.</summary>
    void OpenForge(Navigation.ForgeBrowserMode mode);
}
