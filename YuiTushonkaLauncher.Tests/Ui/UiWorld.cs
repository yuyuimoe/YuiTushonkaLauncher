using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Startup;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Instances;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The real service graph, a temporary data folder and the fake dialogs — one object per test. Building through
/// <see cref="ServiceRegistration.AddLauncherServices" /> is itself part of the test: a page that cannot be resolved is
/// a broken window.
/// </summary>
public sealed class UiWorld : IAsyncDisposable
{
    private readonly TempDirectory _temp;
    private readonly ServiceProvider _services;
    private FakeBaseInstall? _base;

    private UiWorld(TempDirectory temp, ServiceProvider services, FakeDialogService dialogs)
    {
        _temp = temp;
        _services = services;
        Dialogs = dialogs;
        InstancesRoot = temp.Combine("instances");
        System.IO.Directory.CreateDirectory(InstancesRoot);
        Shell = services.GetRequiredService<ShellViewModel>();
    }

    public FakeDialogService Dialogs { get; }

    /// <summary>Where instances go: a folder inside the temporary root.</summary>
    public string InstancesRoot { get; private set; } = string.Empty;

    /// <summary>The structural fake base install the instance was seeded from.</summary>
    public string BaseInstallRoot
    {
        get
        {
            _base ??= FakeBaseInstall.CreateSpt41();
            return _base.Root;
        }
    }

    public ShellViewModel Shell { get; }

    public IServiceProvider Services
    {
        get { return _services; }
    }

    public ILocalizer Localizer
    {
        get { return _services.GetRequiredService<ILocalizer>(); }
    }

    public InstancesPageViewModel Instances
    {
        get { return _services.GetRequiredService<InstancesPageViewModel>(); }
    }

    public ModsPageViewModel Mods
    {
        get { return _services.GetRequiredService<ModsPageViewModel>(); }
    }

    public LogsPageViewModel Logs
    {
        get { return _services.GetRequiredService<LogsPageViewModel>(); }
    }

    public SettingsPageViewModel Settings
    {
        get { return _services.GetRequiredService<SettingsPageViewModel>(); }
    }

    public ServerPageViewModel Server
    {
        get { return _services.GetRequiredService<ServerPageViewModel>(); }
    }

    /// <summary>Creates a real instance from a structural fake base install, through the real service.</summary>
    public async Task<Instance> SeedInstanceAsync(string name)
    {
        _base ??= FakeBaseInstall.CreateSpt41();
        var service = _services.GetRequiredService<IInstanceService>();
        var instance = await service.CreateAsync(
            new InstanceCreationRequest
            {
                Name = name,
                Location = InstancesRoot,
                BaseInstallPath = _base.Root,
            }
        );

        await _services.GetRequiredService<IInstanceContext>().RefreshAsync();
        return instance;
    }

    public TService Get<TService>()
        where TService : notnull
    {
        return _services.GetRequiredService<TService>();
    }

    public static async Task<UiWorld> CreateAsync(string language = "en", Action<IServiceCollection>? configure = null)
    {
        Headless.EnsureStarted();
        var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        var dialogs = new FakeDialogService();
        var services = new ServiceCollection();
        services.AddLauncherServices(paths, language);
        services.AddSingleton<IDialogService>(dialogs);
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        return new UiWorld(temp, provider, dialogs);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _temp.Dispose();
    }
}
