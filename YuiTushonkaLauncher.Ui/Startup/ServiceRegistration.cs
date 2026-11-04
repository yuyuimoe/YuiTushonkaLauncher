using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Infrastructure;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Infrastructure.Processes;
using YuiTushonkaLauncher.Infrastructure.Updates;
using YuiTushonkaLauncher.Localization;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Services;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Startup;

/// <summary>
/// The one place that knows which implementation backs each interface. The shell, its pages and the window come from
/// here too, so a headless test can build the same graph the app builds.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>Registers paths, logging, services and view models.</summary>
    public static IServiceCollection AddLauncherServices(
        this IServiceCollection services,
        IAppPaths paths,
        string? language = null,
        string? version = null
    )
    {
        // Serilog owns the sinks; the Microsoft.Extensions.Logging bridge lets services take ILogger<T>.
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

        services.AddSingleton(paths);
        services.AddLauncherPersistence();
        services.AddLauncherForge();
        services.AddLauncherInstances();
        services.AddLauncherMods();
        services.AddLauncherRuntime();
        services.AddLauncherServer();
        services.AddLauncherLaunch();
        services.AddLauncherLogs();
        services.AddLauncherUpdates(version ?? AppVersion.Current);
        services.AddSingleton<ISingleInstanceGuard, SingleInstanceGuard>();

        services.AddLauncherUi(language);
        return services;
    }

    /// <summary>The UI layer on its own, which is what the headless tests register on top of a fake service set.</summary>
    public static IServiceCollection AddLauncherUi(
        this IServiceCollection services,
        string? language = null,
        Func<Application?>? application = null
    )
    {
        services.AddSingleton<ILocalizer>(_ => new JsonLocalizer(language ?? "en"));
        services.AddSingleton<IThemeController>(_ => new ThemeController(application ?? (() => Application.Current)));
        services.AddSingleton<WindowController>();
        services.AddSingleton<IWindowController>(provider => provider.GetRequiredService<WindowController>());

        services.AddSingleton<IInstanceContext, InstanceContext>();
        services.AddSingleton<ShellNavigation>();
        services.AddSingleton<INavigationService>(provider => provider.GetRequiredService<ShellNavigation>());
        services.AddSingleton<IDownloadActivity, DownloadActivity>();
        services.AddSingleton<IUrlOpener, UrlOpener>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(provider => provider.GetRequiredService<DialogService>());

        services.AddSingleton<LaunchPanelViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<InstancesPageViewModel>();
        services.AddSingleton<ForgePageViewModel>();
        services.AddSingleton<ModsPageViewModel>();
        services.AddSingleton<PatchPageViewModel>();
        services.AddSingleton<ServerPageViewModel>();
        services.AddSingleton<LogsPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services;
    }
}
