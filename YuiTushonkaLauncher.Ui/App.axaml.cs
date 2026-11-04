using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
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
