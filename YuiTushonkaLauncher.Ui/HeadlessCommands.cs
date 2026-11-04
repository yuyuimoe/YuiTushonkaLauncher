using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Core.Patching;
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

    /// <summary>
    /// Installs a local archive or an https download into an instance
    /// (<c>--install-mod ARCHIVE|URL --instance NAME [--forge-id N]</c>).
    /// </summary>
    public const string InstallMod = "--install-mod";

    /// <summary>Scans an instance's mod folders and reports what a migration would register.</summary>
    public const string MigrateScan = "--migrate-scan";

    /// <summary>Plans, then applies, the SPT deltas of an instance (<c>--patch NAME</c>).</summary>
    public const string Patch = "--patch";

    /// <summary>Restores every <c>.spt-bak</c> in an instance (<c>--restore NAME</c>).</summary>
    public const string Restore = "--restore";

    /// <summary>Reports the patch plan of an instance without touching it (<c>--patch-plan NAME</c>).</summary>
    public const string PatchPlan = "--patch-plan";

    /// <summary>Writes a <c>.sptpack</c> from an instance (<c>--export NAME FILE</c>).</summary>
    public const string Export = "--export";

    /// <summary>
    /// Reviews a <c>.sptpack</c> against an instance (<c>--import FILE --instance NAME</c>). Nothing is written
    /// unless <c>--apply</c> is also given.
    /// </summary>
    public const string Import = "--import";

    /// <summary>Runs a headless command when one was requested.</summary>
    /// <returns><c>true</c> when a command ran and the process should exit with <paramref name="exitCode" />.</returns>
    public static bool TryRun(string[] args, IAppPaths paths, out int exitCode)
    {
        exitCode = 0;

        var migrateOnly = args.Contains(MigrateOnly, StringComparer.Ordinal);
        var inspectBaseIndex = IndexOf(args, InspectBase);
        var createInstanceIndex = IndexOf(args, CreateInstance);
        var installModIndex = IndexOf(args, InstallMod);
        var migrateScanIndex = IndexOf(args, MigrateScan);
        var patchIndex = IndexOf(args, Patch);
        var restoreIndex = IndexOf(args, Restore);
        var patchPlanIndex = IndexOf(args, PatchPlan);
        var exportIndex = IndexOf(args, Export);
        var importIndex = IndexOf(args, Import);

        if (
            !migrateOnly
            && inspectBaseIndex < 0
            && createInstanceIndex < 0
            && installModIndex < 0
            && migrateScanIndex < 0
            && patchIndex < 0
            && restoreIndex < 0
            && patchPlanIndex < 0
            && exportIndex < 0
            && importIndex < 0
        )
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

            if (installModIndex >= 0)
            {
                InstallModFromArchive(services, ValueAfter(args, installModIndex));
            }

            if (migrateScanIndex >= 0)
            {
                ScanForMigration(services, ValueAfter(args, migrateScanIndex));
            }

            if (patchPlanIndex >= 0)
            {
                PrintPatchPlan(services, ValueAfter(args, patchPlanIndex));
            }

            if (patchIndex >= 0)
            {
                ApplyPatches(services, ValueAfter(args, patchIndex));
            }

            if (restoreIndex >= 0)
            {
                RestorePatches(services, ValueAfter(args, restoreIndex));
            }

            if (exportIndex >= 0)
            {
                ExportPackage(services, ValueAfter(args, exportIndex), args.ElementAtOrDefault(exportIndex + 2) ?? string.Empty);
            }

            if (importIndex >= 0)
            {
                ImportPackage(services, args, ValueAfter(args, importIndex));
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

    private static void InstallModFromArchive(IServiceProvider services, string archivePath)
    {
        var instanceName = ValueOf(System.Environment.GetCommandLineArgs(), "--instance");
        var instances = services.GetRequiredService<IInstanceService>();
        var installer = services.GetRequiredService<Core.Mods.IModInstaller>();
        var integrity = services.GetRequiredService<Core.Mods.IIntegrityService>();

        var instance = instances.ListAsync().GetAwaiter().GetResult().FirstOrDefault(row => row.Name == instanceName);
        if (instance is null)
        {
            throw new Core.Instances.InstanceValidationException([$"No instance named '{instanceName}'."]);
        }

        // An https address is downloaded (and recorded as a Forge install when --forge-id is given); anything
        // else is a local archive.
        var isDownload = Uri.TryCreate(archivePath, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps;
        var forgeId = int.TryParse(ValueOf(System.Environment.GetCommandLineArgs(), "--forge-id"), out var parsedForgeId)
            ? parsedForgeId
            : (int?)null;

        var result = installer
            .InstallAsync(
                new Core.Mods.ModInstallRequest
                {
                    InstanceId = instance.Id,
                    ArchivePath = isDownload ? null : archivePath,
                    DownloadUrl = isDownload ? url : null,
                    ForgeId = forgeId,
                }
            )
            .GetAwaiter()
            .GetResult();

        Log.Information(
            "Installed {Guid} ({Kind} {Version}) as {Recognition}",
            result.Guid,
            result.Kind,
            result.Version,
            result.Recognition
        );
        Log.Information("  {Count} file(s), {Configs} config file(s)", result.Files.Count, result.ConfigFiles.Count);

        foreach (var file in result.Files.Take(8))
        {
            Log.Information("  + {File}", file);
        }

        foreach (var warning in result.Warnings)
        {
            Log.Warning("  warning: {Warning}", warning);
        }

        var report = integrity.VerifyAsync(instance.Id).GetAwaiter().GetResult();
        Log.Information("  integrity after install: clean={Clean}", report.IsClean);
    }

    private static void ScanForMigration(IServiceProvider services, string instanceName)
    {
        var instances = services.GetRequiredService<IInstanceService>();
        var migration = services.GetRequiredService<Core.Instances.IMigrationService>();

        var instance = instances.ListAsync().GetAwaiter().GetResult().FirstOrDefault(row => row.Name == instanceName);
        if (instance is null)
        {
            throw new Core.Instances.InstanceValidationException([$"No instance named '{instanceName}'."]);
        }

        var plan = migration.ScanAsync(instance.Id).GetAwaiter().GetResult();

        Log.Information(
            "Migration scan of {Path}: {Recognized} recognized, {Unknown} unknown to Forge, {NoGuid} without GUID, {Orphans} orphan file(s)",
            plan.InstancePath,
            plan.Recognized.Count,
            plan.Unrecognized.Count,
            plan.WithoutGuid.Count,
            plan.OrphanFiles.Count
        );

        foreach (var entry in plan.Recognized.Concat(plan.Unrecognized).Concat(plan.WithoutGuid))
        {
            Log.Information(
                "  {Match}: {Name} ({Guid}) {Files} file(s)",
                entry.Match,
                entry.Name,
                entry.Guid ?? "<no guid>",
                entry.Files.Count
            );
        }

        foreach (var warning in plan.Warnings)
        {
            Log.Warning("  warning: {Warning}", warning);
        }
    }

    private static void PrintPatchPlan(IServiceProvider services, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var patching = services.GetRequiredService<IPatchService>();
        var plan = patching.PlanAsync(instance.Id).GetAwaiter().GetResult();

        Log.Information("Patch plan for {Instance}: {Summary}", instance.Name, plan.Summary);

        foreach (var file in plan.Files)
        {
            Log.Information(
                "  {Action}: {File} ({Size} bytes now, delta {Input:N0} -> {Output:N0})",
                file.Action,
                file.RelativePath,
                file.CurrentSize?.ToString("N0") ?? "missing",
                file.ExpectedInputSize,
                file.ExpectedOutputSize
            );
            Log.Information("    {Reason}", file.Reason);
        }
    }

    private static void ApplyPatches(IServiceProvider services, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var patching = services.GetRequiredService<IPatchService>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var report = patching
            .PatchAsync(
                instance.Id,
                new Progress<PatchProgress>(progress => Log.Debug("{File}: {Percent:F0}%", progress.FileName, progress.Percentage))
            )
            .GetAwaiter()
            .GetResult();

        stopwatch.Stop();
        Log.Information("Patching {Instance} finished in {Elapsed}: {Summary}", instance.Name, stopwatch.Elapsed, report.Summary);

        foreach (var file in report.Files)
        {
            Log.Information("  {Outcome}: {File} ({Message})", file.Outcome, file.RelativePath, file.Message);
            Log.Information("    sha256 {Before} -> {After}", file.Sha256Before ?? "-", file.Sha256After ?? "-");
        }

        if (!report.Succeeded)
        {
            throw new InstanceValidationException([$"Patching {instance.Name} did not finish cleanly: {report.Summary}"]);
        }
    }

    private static void RestorePatches(IServiceProvider services, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var patching = services.GetRequiredService<IPatchService>();
        var report = patching.RestoreAsync(instance.Id).GetAwaiter().GetResult();

        Log.Information("Restoring {Instance}: {Summary}", instance.Name, report.Summary);

        foreach (var file in report.Files)
        {
            Log.Information("  {Outcome}: {File} ({Message})", file.Outcome, file.RelativePath, file.Message);
        }

        if (!report.Succeeded)
        {
            throw new InstanceValidationException([$"Restoring {instance.Name} did not finish cleanly: {report.Summary}"]);
        }
    }

    private static void ExportPackage(IServiceProvider services, string instanceName, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InstanceValidationException(["Usage: --export <instance> <file.sptpack>"]);
        }

        var instance = FindInstance(services, instanceName);
        var packs = services.GetRequiredService<IPackService>();
        var result = packs.ExportAsync(instance.Id, outputPath).GetAwaiter().GetResult();

        Log.Information(
            "Exported {Instance} to {Path}: {Mods} mod(s), {Addons} addon(s), {Configs} config(s)",
            instance.Name,
            result.PackagePath,
            result.Manifest.Mods.Count,
            result.Manifest.Addons.Count,
            result.Manifest.Configs.Count
        );
        Log.Information("  share code {Code}", result.ShareCode);

        foreach (var skipped in result.Skipped)
        {
            Log.Information("  skipped {Path}: {Reason}", skipped.Path, skipped.Reason);
        }

        foreach (var warning in result.Warnings)
        {
            Log.Warning("  warning: {Warning}", warning);
        }
    }

    private static void ImportPackage(IServiceProvider services, string[] args, string packagePath)
    {
        var instance = FindInstance(services, ValueOf(args, "--instance"));
        var packs = services.GetRequiredService<IPackService>();
        var plan = packs.PlanImportAsync(instance.Id, packagePath).GetAwaiter().GetResult();

        Log.Information(
            "Package {Code} (SPT {Spt}, made by {App}): {Mods} mod(s), {Addons} addon(s), {Configs} config(s)",
            plan.Manifest.ShareCode,
            plan.Manifest.SptVersion,
            plan.Manifest.AppVersion,
            plan.Manifest.Mods.Count,
            plan.Manifest.Addons.Count,
            plan.Manifest.Configs.Count
        );

        foreach (var warning in plan.Warnings)
        {
            Log.Warning("  warning: {Warning}", warning);
        }

        foreach (var item in plan.Items)
        {
            // The name comes from the package and is unauthenticated: always show the GUID next to it.
            Log.Information(
                "  {Action}: {Kind} {Name} [{Id}] {Version}",
                item.Action,
                item.Kind,
                item.Name,
                item.Id,
                item.Version ?? string.Empty
            );
            Log.Information("    {Reason}{Url}", item.Reason, item.DownloadUrl is null ? string.Empty : $" ({item.DownloadUrl})");
        }

        if (!args.Contains("--apply", StringComparer.Ordinal))
        {
            Log.Information("Review only: nothing was written. Add --apply to import.");

            return;
        }

        var options = new ImportOptions
        {
            ReplaceDifferentVersions = args.Contains("--replace", StringComparer.Ordinal),
            OverwriteEditedConfigs = args.Contains("--overwrite-configs", StringComparer.Ordinal),
            AcceptChangedContent = args.Contains("--accept-changed-content", StringComparer.Ordinal),
        };

        var result = packs
            .ImportAsync(instance.Id, packagePath, options, new Progress<string>(message => Log.Information("  {Message}", message)))
            .GetAwaiter()
            .GetResult();

        Log.Information("Import finished: {Summary}", result.Summary);

        foreach (var item in result.Items)
        {
            Log.Information(
                "  {Outcome}: {Kind} {Id} ({Message}){Verified}",
                item.Outcome,
                item.Kind,
                item.Id,
                item.Message,
                item.Verified ? " [verified]" : string.Empty
            );
        }

        if (!result.Succeeded)
        {
            throw new InstanceValidationException([$"The import did not finish cleanly: {result.Summary}"]);
        }
    }

    private static Core.Domain.Instance FindInstance(IServiceProvider services, string instanceName)
    {
        var instances = services.GetRequiredService<IInstanceService>();
        var instance = instances.ListAsync().GetAwaiter().GetResult().FirstOrDefault(row => row.Name == instanceName);

        return instance ?? throw new InstanceValidationException([$"No instance named '{instanceName}'."]);
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
