using System;
using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Infrastructure.Processes;

namespace YuiTushonkaLauncher;

internal static class Program
{
    private static ISingleInstanceGuard? _guard;

    /// <summary>Paths resolved before the DI container exists, so logging can use them too.</summary>
    internal static IAppPaths Paths { get; private set; } = new AppPaths();

    /// <summary>The language asked for on the command line, when there was one; the settings value otherwise.</summary>
    internal static string? LanguageOverride { get; private set; }

    /// <summary>Whether this process owns the instance lock.</summary>
    internal static StartupMode StartupMode { get; private set; } = StartupMode.Normal;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        Paths = new AppPaths();
        LanguageOverride = ReadLanguage(args);
        Paths.EnsureCreated();
        ConfigureLogging(Paths);

        _guard = new SingleInstanceGuard(Paths, NullLogger<SingleInstanceGuard>.Instance);
        if (_guard.TryAcquire())
        {
            Log.Information("Starting YuiTushonkaLauncher {Version}", AppVersion.Current);
            StartupMode = StartupMode.Normal;
        }
        else
        {
            Log.Warning("Another YuiTushonkaLauncher instance is already running");
            StartupMode = StartupMode.AlreadyRunning;
        }

        try
        {
            if (StartupMode == StartupMode.Normal && HeadlessCommands.TryRun(args, Paths, out var headlessExitCode))
            {
                return headlessExitCode;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return StartupMode == StartupMode.Normal ? 0 : ExitCodes.AlreadyRunning;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Launcher terminated unexpectedly");
            return ExitCodes.Crashed;
        }
        finally
        {
            _guard?.Dispose();
            _guard = null;
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont().LogToTrace();
    }

    /// <summary>Reads <c>--lang &lt;culture&gt;</c>. An unknown language is ignored here and resolved by the localizer.</summary>
    private static string? ReadLanguage(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--lang", StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void ConfigureLogging(IAppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                System.IO.Path.Join(paths.LogsDirectory, "launcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 20,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }
}
