using System.IO.Compression;
using System.Text;
using YuiTushonkaLauncher.Core.Packs;

namespace YuiTushonkaLauncher.Infrastructure.Packs;

/// <summary>
/// Writes a <c>.sptpack</c> deterministically: fixed entry order, fixed timestamps, and nothing but the
/// manifest, a generated README and the config files. The file is built next to the destination and moved
/// into place, so a failed export never leaves a half-written package behind.
/// </summary>
public static class PackWriter
{
    /// <summary>The earliest timestamp a zip can hold; using it keeps two exports byte-comparable.</summary>
    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Writes the package. <paramref name="configs" /> maps instance-relative paths to content.</summary>
    public static void Write(string packagePath, PackManifest manifest, IReadOnlyDictionary<string, byte[]> configs)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(packagePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = packagePath + ".part";

        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddEntry(archive, PackFormat.ManifestEntry, PackManifestSerializer.Serialize(manifest));
                AddEntry(archive, PackFormat.ReadmeEntry, Encoding.UTF8.GetBytes(BuildReadme(manifest)));

                foreach (var pair in configs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    AddEntry(archive, PackRules.ConfigEntryName(pair.Key), pair.Value);
                }
            }

            File.Move(temporary, packagePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;

        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static string BuildReadme(PackManifest manifest)
    {
        var builder = new StringBuilder();
        builder.Append("SPT pack ").Append(manifest.ShareCode).Append('\n');
        builder.Append("Made for SPT ").Append(manifest.SptVersion).Append(" (Tarkov ").Append(manifest.TarkovVersion).Append(")\n\n");
        builder.Append("This file contains no mods: only the list of exact versions and the config files that were edited.\n");
        builder.Append("Open it with the launcher to review what would be installed before anything is written.\n\n");

        builder.Append("Mods (").Append(manifest.Mods.Count).Append(")\n");
        foreach (var mod in manifest.Mods)
        {
            builder.Append("  ").Append(mod.Name).Append(' ').Append(mod.Version).Append(" [").Append(mod.Guid).Append("]\n");
        }

        if (manifest.Addons.Count > 0)
        {
            builder.Append("\nAddons (").Append(manifest.Addons.Count).Append(")\n");
            foreach (var addon in manifest.Addons)
            {
                builder
                    .Append("  ")
                    .Append(addon.Name)
                    .Append(' ')
                    .Append(addon.Version)
                    .Append(" -> ")
                    .Append(addon.ParentGuid)
                    .Append('\n');
            }
        }

        if (manifest.Configs.Count > 0)
        {
            builder.Append("\nConfig files (").Append(manifest.Configs.Count).Append(")\n");
            foreach (var config in manifest.Configs)
            {
                builder.Append("  ").Append(config.Path).Append('\n');
            }
        }

        return builder.ToString();
    }
}
