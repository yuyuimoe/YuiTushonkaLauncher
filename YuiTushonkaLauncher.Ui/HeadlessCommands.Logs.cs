using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Logs;

namespace YuiTushonkaLauncher;

/// <summary>Headless commands for the log layer (phase 11, 11.3).</summary>
internal static partial class HeadlessCommands
{
    /// <summary>
    /// Lists an instance's log files and tails the newest of each source
    /// (<c>--logs NAME [--kind game|server|bepinex|launcher] [--lines N] [--export FILE]</c>).
    /// </summary>
    public const string Logs = "--logs";

    /// <summary>Writes the crash bundle of an instance (<c>--diagnostics NAME [--output FILE]</c>).</summary>
    public const string Diagnostics = "--diagnostics";

    /// <summary>Applies log retention to the launcher's own logs (<c>--logs-prune</c>).</summary>
    public const string LogsPrune = "--logs-prune";

    private static bool LogCommandRequested(string[] args)
    {
        return IndexOf(args, Logs) >= 0 || IndexOf(args, Diagnostics) >= 0 || IndexOf(args, LogsPrune) >= 0;
    }

    private static void RunLogCommands(IServiceProvider services, string[] args)
    {
        if (IndexOf(args, LogsPrune) >= 0)
        {
            PruneLogs(services);
        }

        if (IndexOf(args, Logs) is var logsIndex and >= 0)
        {
            PrintLogs(services, args, ValueAfter(args, logsIndex));
        }

        if (IndexOf(args, Diagnostics) is var diagnosticsIndex and >= 0)
        {
            WriteDiagnostics(services, args, ValueAfter(args, diagnosticsIndex));
        }
    }

    private static void PrintLogs(IServiceProvider services, string[] args, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var service = services.GetRequiredService<ILogService>();
        var kind = Enum.TryParse<LogSourceKind>(ValueOf(args, "--kind"), ignoreCase: true, out var parsed) ? parsed : (LogSourceKind?)null;
        var lines = int.TryParse(ValueOf(args, "--lines"), out var parsedLines) ? parsedLines : LogLimits.DefaultTailLines;
        var export = NullIfEmpty(ValueOf(args, "--export"));

        var files = service.ListAsync(instance.Id).GetAwaiter().GetResult();
        var selected = (kind is null ? files.Where(file => file.IsDefault) : files.Where(file => file.Kind == kind)).ToList();

        Log.Information("Log files of '{Instance}' ({Total} found, {Shown} shown):", instance.Name, files.Count, selected.Count);

        foreach (var file in files)
        {
            Log.Information(
                "  {Kind,-8} {Size,10} {When:u} {Default} {Path}",
                file.Kind,
                Size(file.Bytes),
                file.LastWrite,
                file.IsDefault ? "default" : "       ",
                file.Path
            );
        }

        if (export is not null)
        {
            var all = new List<LogLine>();

            foreach (var file in selected)
            {
                var tail = service.ReadTailAsync(file, lines).GetAwaiter().GetResult();

                if (tail.TooLarge)
                {
                    Log.Warning("  {Reason}", tail.Reason);
                    continue;
                }

                all.AddRange(tail.Lines);
            }

            var written = service.ExportLinesAsync(all, export).GetAwaiter().GetResult();
            Log.Information("Wrote {Count} line(s) to {Path}", all.Count, written);
            return;
        }

        foreach (var file in selected)
        {
            var tail = service.ReadTailAsync(file, lines).GetAwaiter().GetResult();

            if (tail.TooLarge || tail.Missing)
            {
                Log.Warning("  {File}: {Reason}", file.Label, tail.Reason);
                continue;
            }

            Log.Information("  --- {Kind} {Label}: {Count} line(s) ---", file.Kind, file.Label, tail.Lines.Count);

            foreach (var line in tail.Lines)
            {
                Log.Information("  | {Line}", line.Text);
            }
        }
    }

    private static void WriteDiagnostics(IServiceProvider services, string[] args, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var destination =
            NullIfEmpty(ValueOf(args, "--output"))
            ?? Path.Combine(
                services.GetRequiredService<Core.Abstractions.IAppPaths>().LogsDirectory,
                $"diagnostics-{instance.Name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt"
            );

        var service = services.GetRequiredService<ILogService>();
        var written = service.ExportDiagnosticsAsync(instance.Id, destination).GetAwaiter().GetResult();

        Log.Information("Diagnostics for '{Instance}' written to {Path}", instance.Name, written);
    }

    private static void PruneLogs(IServiceProvider services)
    {
        var retention = services.GetRequiredService<ILogRetention>();
        var paths = services.GetRequiredService<Core.Abstractions.IAppPaths>();
        var settings = services.GetRequiredService<Core.Abstractions.ISettingsService>().LoadAsync().GetAwaiter().GetResult();
        var plan = retention
            .ApplyAsync(paths.LogsDirectory, settings.LogRetentionFiles, settings.LogRetentionDays)
            .GetAwaiter()
            .GetResult();

        Log.Information(
            "Log retention: {Compressed} file(s) compressed into archive/, {Deleted} deleted, {Freed} freed",
            plan.ToCompress.Count,
            plan.ToDelete.Count,
            Size(plan.BytesFreed)
        );
    }

    private static string Size(long bytes)
    {
        return bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.0} MiB" : $"{bytes / 1024.0:0.0} KiB";
    }
}
