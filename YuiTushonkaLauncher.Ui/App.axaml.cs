using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Services;
using YuiTushonkaLauncher.Startup;
using YuiTushonkaLauncher.ViewModels;
using YuiTushonkaLauncher.Views;

namespace YuiTushonkaLauncher;

public partial class App : Application
{
    private ServiceProvider? _services;
    private HomeViewModel? _home;

    private ShellViewModel? _shell;

    private InstanceManagerWindow? _manager;

    private ForgeWindow? _forge;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.StartupMode == StartupMode.AlreadyRunning)
            {
                desktop.MainWindow = new AlreadyRunningWindow();
            }
            else
            {
                StartDesktop(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _services = new ServiceCollection().AddLauncherServices(Program.Paths, Program.LanguageOverride).BuildServiceProvider();

        // Migrations must finish before any view model touches the database. Avalonia's startup hook is synchronous,
        // so this is the one place a blocking wait is correct.
        Log.Information("Startup: applying database migrations");
        // On a pool thread: a blocking wait is only safe when the work does not need this thread's message loop.
        Task.Run(() => _services.GetRequiredService<DatabaseInitializer>().InitializeAsync()).GetAwaiter().GetResult();

        Log.Information("Startup: reading settings");
        var settings = _services.GetRequiredService<Core.Abstractions.ISettingsService>();
        _services.GetRequiredService<IThemeController>().Apply(settings.Current.Theme);

        Log.Information("Startup: building the shell");
        _home = _services.GetRequiredService<HomeViewModel>();
        Log.Information("Startup: creating the window");
        var window = new MainWindow { DataContext = _home };
        _services.GetRequiredService<DialogService>().Attach(window);

        // The manager window is built the first time it is asked for, so a launcher that only lists instances never
        // pays for the mods, server and log pages.
        var windows = _services.GetRequiredService<WindowController>();
        windows.Attach(window);
        windows.AttachManager(OpenManager);
        windows.AttachForge(OpenForge);

        // A detached pair survives the launcher, so closing with one running asks first (D22).
        window.Closing += OnClosing;
        desktop.MainWindow = window;
        Log.Information("Startup: window ready");
        desktop.Exit += OnDesktopExit;

        _ = LoadAsync(_home);
    }

    /// <summary>Creates the manager window on first use and navigates it to the section that asked for it.</summary>
    private Window? OpenManager(Navigation.LauncherSection section)
    {
        if (_services is null)
        {
            return null;
        }

        if (_manager is null)
        {
            Log.Information("Startup: building the instance manager");
            _shell = _services.GetRequiredService<ShellViewModel>();
            _manager = new InstanceManagerWindow { DataContext = _shell };
        }

        _shell?.Navigate(section);
        return _manager;
    }

    /// <summary>Shows the Forge window, switching it between browsing mods and browsing addons.</summary>
    private Window? OpenForge(Navigation.ForgeBrowserMode mode)
    {
        if (_services is null)
        {
            return null;
        }

        var page = _services.GetRequiredService<ForgePageViewModel>();
        if (_forge is null)
        {
            Log.Information("Startup: building the Forge window");
            _forge = new ForgeWindow { DataContext = page };
        }

        page.Mode = mode;
        return _forge;
    }

    private static async Task LoadAsync(HomeViewModel home)
    {
        try
        {
            await home.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A page that could not load shows its own error; this is the safety net that keeps the window open.
            Log.Error(exception, "The shell failed to load");
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        Log.Information("Startup: reading settings");
        var settings = _services.GetRequiredService<Core.Abstractions.ISettingsService>();
        if (!settings.Current.AskBeforeStoppingProcesses || HasRunningProcesses())
        {
            // Asking is the UI's job on the running state; without a running pair there is nothing to ask.
            if (HasRunningProcesses())
            {
                e.Cancel = true;
                _ = ConfirmCloseAsync();
            }
        }
    }

    /// <summary>True while the game, the server or a launch is in flight, whichever window painted it.</summary>
    private bool HasRunningProcesses()
    {
        return _shell?.HasRunningProcesses() == true || _home?.LaunchPanel.HasRunningProcesses() == true;
    }

    private async Task ConfirmCloseAsync()
    {
        if (_services is null || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var localizer = _services.GetRequiredService<Core.Localization.ILocalizer>();
        var dialogs = _services.GetRequiredService<IDialogService>();
        var dialog = new ConfirmDialogViewModel(localizer)
        {
            Title = localizer["launch.stop"],
            Body = localizer["launch.running"],
            ConfirmLabel = localizer["common.close"],
        };

        if (await dialogs.ConfirmAsync(dialog))
        {
            desktop.Shutdown();
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;
        Log.CloseAndFlush();
    }
}
