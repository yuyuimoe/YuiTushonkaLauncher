using System;
using System.Linq;
using System.Threading.Tasks;
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
        // --version answers before anything else exists: no logging file, no database, no Avalonia.
        if (args.Any(argument => argument.Equals("--version", StringComparison.Ordinal) || argument.Equals("-v", StringComparison.Ordinal)))
        {
            Console.Out.WriteLine(AppVersion.Current);
            return 0;
        }

        // Nothing may die silently: an exception on a background thread or a task nobody awaited is a bug report, and
        // the report starts in this file.
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Log.Fatal(eventArgs.ExceptionObject as Exception, "Unhandled exception on a background thread");
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Error(eventArgs.Exception, "Unobserved task exception");
            eventArgs.SetObserved();
        };

        Paths = new AppPaths();
        LanguageOverride = ReadLanguage(args);
        Paths.EnsureCreated();
        ConfigureLogging(Paths, ReadVerbose(args));

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

    /// <summary>Reads <c>--verbose</c>, which copies the debug lines to the console as well as the file.</summary>
    private static bool ReadVerbose(string[] args)
    {
        return args.Any(argument => argument is "--verbose" or "-v");
    }

    /// <summary>
    /// The file always keeps Debug: a report about a launcher that sat on a step needs the line before the step, not
    /// only the lines that were loud enough for a console. The console stays quiet unless asked.
    /// </summary>
    private static void ConfigureLogging(IAppPaths paths, bool verbose)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(restrictedToMinimumLevel: verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .WriteTo.File(
                System.IO.Path.Join(paths.LogsDirectory, "launcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 20,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }
}
