using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Logs;

/// <summary>
/// Writes what a bug report needs: a selection of lines exactly as they were read, and a diagnostics bundle that puts
/// the versions, the instance state and the last words of every source in one file.
/// </summary>
public sealed class LogService(
    IUnitOfWork unitOfWork,
    ILogCatalog catalog,
    ILogReader reader,
    IInstanceService instances,
    TimeProvider time
) : ILogService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<LogFile>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return catalog.ListAsync(instanceId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LogTail> ReadTailAsync(
        LogFile file,
        int maxLines = LogLimits.DefaultTailLines,
        CancellationToken cancellationToken = default
    )
    {
        return reader.ReadTailAsync(file, maxLines, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> ExportLinesAsync(
        IReadOnlyList<LogLine> lines,
        string destination,
        CancellationToken cancellationToken = default
    )
    {
        var text = new StringBuilder();

        foreach (var line in lines)
        {
            text.Append(line.Kind).Append('\t').Append(line.File).Append(':').Append(line.Number).Append('\t').AppendLine(line.Text);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        await File.WriteAllTextAsync(destination, text.ToString(), new UTF8Encoding(false), cancellationToken);

        return destination;
    }

    /// <inheritdoc />
    public async Task<string> ExportDiagnosticsAsync(Guid instanceId, string destination, CancellationToken cancellationToken = default)
    {
        var instance =
            (await instances.ListAsync(cancellationToken)).FirstOrDefault(row => row.Id == instanceId)
            ?? throw new InvalidOperationException("That instance does not exist.");

        var text = new StringBuilder();
        text.AppendLine("YuiTushonkaLauncher diagnostics");
        text.AppendLine($"Generated: {time.GetUtcNow():u}");
        text.AppendLine();

        text.AppendLine($"Instance: {instance.Name}");
        text.AppendLine($"  path: {instance.Path}");
        text.AppendLine($"  layout: {instance.Layout} ({InstancePaths.Detect(instance.Path)?.ToString() ?? "unknown"})");
        text.AppendLine($"  SPT: {instance.SptVersion}");
        text.AppendLine($"  Tarkov: {instance.TarkovVersion}");
        text.AppendLine($"  port: {instance.ServerPort}");
        text.AppendLine($"  patched: {instance.Patch.IsPatched} (set {instance.Patch.PatchSetVersion ?? "unknown"})");
        text.AppendLine();

        text.AppendLine("Files:");
        foreach (
            var relative in (string[])
                [InstancePaths.GameExecutable, InstancePaths.PatchedAssembly, "winhttp.dll", "BepInEx/core/BepInEx.Preloader.dll"]
        )
        {
            var path = Path.Combine(instance.Path, relative);
            text.AppendLine($"  {relative}: {(File.Exists(path) ? HashOf(path) : "MISSING")}");
        }

        text.AppendLine();
        text.AppendLine("Mods:");

        await foreach (var mod in ModsAsync(instanceId, cancellationToken))
        {
            text.Append(mod).AppendLine();
        }

        text.AppendLine();
        text.AppendLine("Last lines of every source:");

        foreach (var file in (await catalog.ListAsync(instanceId, cancellationToken)).Where(file => file.IsDefault))
        {
            var tail = await reader.ReadTailAsync(file, 40, cancellationToken);
            text.AppendLine();
            text.AppendLine($"--- {file.Kind} {file.Label} ({(tail.TooLarge ? tail.Reason : $"{tail.Lines.Count} lines")}) ---");

            foreach (var line in tail.Lines)
            {
                text.AppendLine(line.Text);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        await File.WriteAllTextAsync(destination, text.ToString(), new UTF8Encoding(false), cancellationToken);

        return destination;
    }

    private async IAsyncEnumerable<string> ModsAsync(
        Guid instanceId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var mods = await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .InstalledMods.AsNoTracking()
                    .Where(mod => mod.InstanceId == instanceId)
                    .OrderBy(mod => mod.Guid)
                    .Select(mod => new
                    {
                        mod.Guid,
                        mod.Name,
                        Version = mod.Version,
                        mod.Kind,
                        mod.IsPinned,
                        Parent = mod.ParentModGuid,
                    })
                    .ToListAsync(token),
            cancellationToken
        );

        foreach (var mod in mods)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"  {mod.Guid} {mod.Version} [{mod.Kind}{(mod.IsPinned ? ", pinned" : string.Empty)}{(mod.Parent is null ? string.Empty : $", addon of {mod.Parent}")}] {mod.Name}"
            );
        }
    }

    private static string HashOf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream)) + $" ({new FileInfo(path).Length} bytes)";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "UNREADABLE: " + exception.Message;
        }
    }
}
