using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.Instances;
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

    /// <summary>Inspects an SPT install and prints what the launcher detected, then exits.</summary>
    public const string InspectBase = "--inspect-base";

    /// <summary>Creates an instance from a base install (<c>--create-instance NAME --from PATH --into PATH</c>).</summary>
    public const string CreateInstance = "--create-instance";

    /// <summary>Runs a headless command when one was requested.</summary>
    /// <returns><c>true</c> when a command ran and the process should exit with <paramref name="exitCode" />.</returns>
    public static bool TryRun(string[] args, IAppPaths paths, out int exitCode)
    {
        exitCode = 0;

        var migrateOnly = args.Contains(MigrateOnly, StringComparer.Ordinal);
        var inspectBaseIndex = IndexOf(args, InspectBase);
        var createInstanceIndex = IndexOf(args, CreateInstance);

        if (!migrateOnly && inspectBaseIndex < 0 && createInstanceIndex < 0)
        {
            return false;
        }

        using var services = new ServiceCollection().AddLauncherServices(paths).BuildServiceProvider();

        try
        {
            services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();

            if (inspectBaseIndex >= 0)
            {
                InspectBaseInstall(services, ValueAfter(args, inspectBaseIndex));
            }

            if (createInstanceIndex >= 0)
            {
                CreateInstanceFromBase(services, args, ValueAfter(args, createInstanceIndex));
            }

            Log.Information("Headless command finished; exiting");
            return true;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Headless command failed");
            exitCode = ExitCodes.Crashed;
            return true;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void InspectBaseInstall(IServiceProvider services, string basePath)
    {
        var probe = services.GetRequiredService<BaseInstallProbe>();
        var detector = services.GetRequiredService<PatchStateDetector>();

        var info = probe.Inspect(basePath);
        var patch = detector.Detect(info.RootPath);

        Log.Information("Base install: {Root}", info.RootPath);
        Log.Information("  layout: {Layout}", info.Layout);
        Log.Information("  SPT: {SptVersion}", info.SptVersion?.ToString() ?? "unknown");
        Log.Information("  Tarkov: {TarkovVersion}", info.TarkovVersion ?? "unknown");
        Log.Information("  patch state: {State} ({Reason})", patch.State, patch.Reason);

        foreach (var warning in info.Warnings)
        {
            Log.Warning("  warning: {Warning}", warning);
        }
    }

    private static void CreateInstanceFromBase(IServiceProvider services, string[] args, string name)
    {
        var instances = services.GetRequiredService<IInstanceService>();
        var request = new InstanceCreationRequest
        {
            Name = name,
            Location = ValueOf(args, "--into"),
            BaseInstallPath = ValueOf(args, "--from"),
        };

        var plan = instances.PlanAsync(request).GetAwaiter().GetResult();
        Log.Information(
            "Plan: {Links} linked ({LinkMiB} MiB), {Copies} copied ({CopyMiB} MiB), {Skips} skipped, {Directories} directories",
            plan.LinkCount,
            plan.LinkBytes / 1024 / 1024,
            plan.CopyCount,
            plan.CopyBytes / 1024 / 1024,
            plan.SkipCount,
            plan.Directories.Count
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var instance = instances.CreateAsync(request).GetAwaiter().GetResult();
        stopwatch.Stop();

        Log.Information(
            "Created {Name} at {Path} in {Elapsed} (patch state stored: {IsPatched})",
            instance.Name,
            instance.Path,
            stopwatch.Elapsed,
            instance.Patch.IsPatched
        );
    }

    private static int IndexOf(string[] args, string name)
    {
        return Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.Ordinal));
    }

    private static string ValueAfter(string[] args, int index)
    {
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
    }

    private static string ValueOf(string[] args, string name)
    {
        return ValueAfter(args, IndexOf(args, name));
    }
}
