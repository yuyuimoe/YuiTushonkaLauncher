using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Startup;

namespace YuiTushonkaLauncher;

/// <summary>
/// Startup work that must also be runnable without a display: CI smoke tests, troubleshooting, and
/// packaging scripts. Avalonia is never touched from here.
/// </summary>
internal static class HeadlessCommands
{
    /// <summary>Applies migrations, prepares the database and exits.</summary>
    public const string MigrateOnly = "--migrate-only";

    /// <summary>Runs a headless command when one was requested.</summary>
    /// <returns><c>true</c> when a command ran and the process should exit with <paramref name="exitCode" />.</returns>
    public static bool TryRun(string[] args, IAppPaths paths, out int exitCode)
    {
        exitCode = 0;

        if (!args.Contains(MigrateOnly, StringComparer.Ordinal))
        {
            return false;
        }

        using var services = new ServiceCollection().AddLauncherServices(paths).BuildServiceProvider();

        try
        {
            services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
            Log.Information("Headless migration finished; exiting");
            return true;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Headless migration failed");
            exitCode = ExitCodes.Crashed;
            return true;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
