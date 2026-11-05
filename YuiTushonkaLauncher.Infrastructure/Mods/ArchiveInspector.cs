using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Mods;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Classifies a mod archive's entries without extracting anything, so the installer can decide the
/// mod kind, which assembly to identify and where each file belongs before touching the instance.
/// </summary>
public sealed class ArchiveInspector(ILogger<ArchiveInspector> logger) : IArchiveInspector
{
    private static readonly string[] LayoutRoots = ["BepInEx", "SPT_Runtime", "EscapeFromTarkov_Data", "user", "SPT"];

    /// <inheritdoc />
    public ArchiveInspection Inspect(string archivePath)
    {
        // The format is read from the bytes: a mod can arrive as zip, 7z or rar and the name says nothing.
        using var archive = ModArchiveContent.Open(archivePath);
        var archiveEntries = archive.Entries;
        var entries = new List<ArchiveEntry>(archiveEntries.Count);
        var unsafeEntries = new List<string>();

        foreach (var entry in archiveEntries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            // A link is not a file a mod may bring: it points somewhere, and the launcher would either write a useless
            // empty file or follow it out of the instance.
            if (!string.IsNullOrEmpty(entry.LinkTarget))
            {
                unsafeEntries.Add($"{entry.Key} (symbolic link)");
                continue;
            }

            // Rootedness has to be judged before normalisation strips the leading separator.
            var raw = entry.Key ?? string.Empty;

            if (IsUnsafe(raw, out var reason))
            {
                unsafeEntries.Add($"{raw} ({reason})");
                entries.Add(
                    new ArchiveEntry
                    {
                        RelativePath = Normalize(raw),
                        Kind = ArchiveEntryKind.Unsafe,
                        Length = entry.Size,
                    }
                );
                continue;
            }

            entries.Add(
                new ArchiveEntry
                {
                    RelativePath = Normalize(raw),
                    Kind = ArchiveEntryKind.Content,
                    Length = entry.Size,
                }
            );
        }

        // The wrapper folder comes off before anything is classified: a mod shipped as "ModName/BepInEx/plugins/x.dll"
        // is a client mod, and judging it by the wrapped path called it content and put its plugin nowhere.
        var rootPrefix = FindRootPrefix(entries);
        if (rootPrefix is not null)
        {
            entries =
            [
                .. entries.Select(entry =>
                    entry with
                    {
                        RelativePath = StripPrefix(entry.RelativePath, rootPrefix),
                        NeedsRelocation = true,
                    }
                ),
            ];
        }

        entries =
        [
            .. entries.Select(entry =>
            {
                if (entry.Kind == ArchiveEntryKind.Unsafe)
                {
                    return entry;
                }

                var kind = Classify(entry.RelativePath);
                if (kind == ArchiveEntryKind.Unsafe)
                {
                    unsafeEntries.Add($"{entry.RelativePath} (ships a patched game binary)");
                }

                return entry with
                {
                    Kind = kind,
                };
            }),
        ];

        var client = entries.Count(entry => entry.Kind == ArchiveEntryKind.ClientAssembly);
        var server = entries.Count(entry => entry.Kind == ArchiveEntryKind.ServerAssembly);

        var kind = (client > 0, server > 0) switch
        {
            (true, true) => ModKind.Both,
            (true, false) => ModKind.Client,
            (false, true) => ModKind.Server,
            _ => ModKind.Client,
        };

        var candidates = entries
            .Where(entry =>
                entry.Kind is ArchiveEntryKind.ServerAssembly or ArchiveEntryKind.ClientAssembly or ArchiveEntryKind.GameAssembly
            )
            .OrderBy(entry =>
                entry.Kind == ArchiveEntryKind.ServerAssembly ? 0
                : entry.Kind == ArchiveEntryKind.ClientAssembly ? 1
                : 2
            )
            .Select(entry => entry.RelativePath)
            .ToList();

        var inspection = new ArchiveInspection
        {
            ArchivePath = archivePath,
            Entries = entries,
            RootPrefix = rootPrefix,
            Kind = kind,
            CandidateAssemblies = candidates,
            UnsafeEntries = unsafeEntries,
        };

        logger.LogInformation(
            "Inspected {Archive} ({Format}): {Kind}, {Entries} entries ({Client} client, {Server} server assemblies), root prefix '{Prefix}'",
            Path.GetFileName(archivePath),
            ModArchiveContent.Describe(archive.Format),
            kind,
            entries.Count,
            client,
            server,
            rootPrefix ?? "<none>"
        );

        return inspection;
    }

    private static ArchiveEntryKind Classify(string relativePath)
    {
        var path = relativePath;

        // A mod shipping its own patched game binary can never be trusted.
        if (path.EndsWith("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveEntryKind.Unsafe;
        }

        if (path.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase))
        {
            return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ArchiveEntryKind.ClientAssembly : ArchiveEntryKind.Content;
        }

        if (
            path.StartsWith("SPT_Runtime/user/mods/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("SPT/user/mods/", StringComparison.OrdinalIgnoreCase)
        )
        {
            return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ArchiveEntryKind.ServerAssembly : ArchiveEntryKind.Content;
        }

        if (path.StartsWith("EscapeFromTarkov_Data/Managed/", StringComparison.OrdinalIgnoreCase))
        {
            return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ArchiveEntryKind.GameAssembly : ArchiveEntryKind.Content;
        }

        if (path.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase) || IsConfigFile(path))
        {
            return ArchiveEntryKind.Config;
        }

        // A bare assembly at the root is a mod that assumes the installer puts it in the right place.
        if (!path.Contains('/') && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveEntryKind.ClientAssembly;
        }

        return ArchiveEntryKind.Content;
    }

    private static bool IsUnsafe(string relativePath, out string reason)
    {
        reason = string.Empty;

        if (Path.IsPathRooted(relativePath) || (relativePath.Length > 1 && relativePath[1] == ':'))
        {
            reason = "absolute path";
            return true;
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment => segment == ".."))
        {
            reason = "path traversal";
            return true;
        }

        if (segments.Any(segment => segment.Length == 0))
        {
            reason = "empty path segment";
            return true;
        }

        return false;
    }

    private static string? FindRootPrefix(IReadOnlyList<ArchiveEntry> entries)
    {
        var firstSegments = entries.Select(entry => entry.RelativePath.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (firstSegments.Count != 1)
        {
            return null;
        }

        var candidate = firstSegments[0];
        if (LayoutRoots.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        // Only a real wrapper directory counts: every entry must live below it.
        return entries.All(entry => entry.RelativePath.Contains('/')) ? candidate : null;
    }

    private static string StripPrefix(string relativePath, string prefix)
    {
        return relativePath.Length > prefix.Length + 1 ? relativePath[(prefix.Length + 1)..] : relativePath;
    }

    private static bool IsConfigFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".json" or ".ini" or ".cfg" or ".toml" or ".yaml" or ".yml" or ".xml" => true,
            _ => false,
        };
    }

    private static string Normalize(string fullName)
    {
        return fullName.Replace('\\', '/').TrimStart('/');
    }
}
