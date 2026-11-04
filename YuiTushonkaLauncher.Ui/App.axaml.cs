using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Startup;
using YuiTushonkaLauncher.ViewModels;
using YuiTushonkaLauncher.Views;

namespace YuiTushonkaLauncher;

public partial class App : Application
{
    private ServiceProvider? _services;

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
                _services = new ServiceCollection().AddLauncherServices(Program.Paths).BuildServiceProvider();

                // Migrations must finish before any view model touches the database. Avalonia's
                // startup hook is synchronous, so this is the one place a blocking wait is correct.
                _services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();

                desktop.MainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
                desktop.Exit += OnDesktopExit;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;
        Log.CloseAndFlush();
    }
}
