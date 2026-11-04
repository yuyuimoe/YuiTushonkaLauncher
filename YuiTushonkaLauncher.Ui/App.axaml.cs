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
    private ShellViewModel? _shell;

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
        _services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();

        var settings = _services.GetRequiredService<Core.Abstractions.ISettingsService>();
        _services.GetRequiredService<IThemeController>().Apply(settings.Current.Theme);

        _shell = _services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow { DataContext = _shell };
        _services.GetRequiredService<DialogService>().Attach(window);
        _services.GetRequiredService<WindowController>().Attach(window);

        // A detached pair survives the launcher, so closing with one running asks first (D22).
        window.Closing += OnClosing;
        desktop.MainWindow = window;
        desktop.Exit += OnDesktopExit;

        _ = LoadAsync(_shell);
    }

    private static async Task LoadAsync(ShellViewModel shell)
    {
        try
        {
            await shell.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A page that could not load shows its own error; this is the safety net that keeps the window open.
            Log.Error(exception, "The shell failed to load");
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shell is null || _services is null)
        {
            return;
        }

        var settings = _services.GetRequiredService<Core.Abstractions.ISettingsService>();
        if (!settings.Current.AskBeforeStoppingProcesses || _shell.HasRunningProcesses())
        {
            // Asking is the UI's job on the running state; without a running pair there is nothing to ask.
            if (_shell.HasRunningProcesses())
            {
                e.Cancel = true;
                _ = ConfirmCloseAsync(_shell);
            }
        }
    }

    private async Task ConfirmCloseAsync(ShellViewModel shell)
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
