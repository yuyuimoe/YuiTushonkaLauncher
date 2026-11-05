using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher;

/// <summary>Headless command for the layout change of phase 11 (11.0).</summary>
internal static partial class HeadlessCommands
{
    private static void MigrateLayouts(IServiceProvider services, string[] args)
    {
        var service = services.GetRequiredService<ILayoutMigrationService>();
        var name = ValueAfter(args, IndexOf(args, MigrateLayout));
        var apply = args.Contains("--apply", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(name))
        {
            var pending = service.ListPendingAsync().GetAwaiter().GetResult();

            if (pending.Count == 0)
            {
                Log.Information("No instance needs the layout migration: all of them already have SPT's merged root.");
                return;
            }

            Log.Information("{Count} instance(s) still use the split layout:", pending.Count);

            foreach (var plan in pending)
            {
                Log.Information(
                    "  {Name}: {Files} file(s) to move, {Bytes} bytes ({Path})",
                    plan.InstanceName,
                    plan.Entries.Count,
                    Fmt(plan.BytesMoved),
                    plan.InstancePath
                );

                foreach (var issue in plan.Issues)
                {
                    Log.Warning("    {Issue}", issue);
                }
            }

            if (!apply)
            {
                Log.Information("Nothing changed. Run --migrate-layout <name> --apply to move one instance.");
                return;
            }

            foreach (var plan in pending.Where(plan => plan.CanMigrate))
            {
                var result = service.MigrateAsync(plan.InstanceId).GetAwaiter().GetResult();
                Log.Information(
                    "  migrated {Name}: {Moved} moved, {Rewritten} stored path(s) rewritten",
                    plan.InstanceName,
                    result.FilesMoved,
                    result.StoredPathsRewritten
                );

                foreach (var warning in result.Warnings)
                {
                    Log.Warning("    {Warning}", warning);
                }
            }

            return;
        }

        var instance = FindInstance(services, name);
        var single = service.PlanAsync(instance.Id).GetAwaiter().GetResult();

        if (single.IsMigrated)
        {
            Log.Information("'{Name}' already has SPT's merged root.", instance.Name);
            return;
        }

        Log.Information(
            "'{Name}': {Files} file(s), {Bytes} bytes, {Stored} stored path(s) to rewrite",
            instance.Name,
            single.Entries.Count,
            Fmt(single.BytesMoved),
            single.StoredPathsToRewrite
        );

        foreach (var issue in single.Issues)
        {
            Log.Error("  {Issue}", issue);
        }

        if (!apply)
        {
            Log.Information("Nothing changed. Add --apply to migrate.");
            return;
        }

        var migrated = service.MigrateAsync(instance.Id).GetAwaiter().GetResult();
        Log.Information(
            "Migrated '{Name}': {Moved} file(s) moved, {Folders} folder(s) removed, {Paths} stored path(s) rewritten",
            instance.Name,
            migrated.FilesMoved,
            migrated.DirectoriesRemoved,
            migrated.StoredPathsRewritten
        );

        foreach (var warning in migrated.Warnings)
        {
            Log.Warning("  {Warning}", warning);
        }
    }

    private static string Fmt(long bytes)
    {
        return bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.0} MiB" : $"{bytes / 1024.0:0.0} KiB";
    }
}
