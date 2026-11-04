using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Infrastructure.Packs;

namespace YuiTushonkaLauncher.Tests.Packs;

/// <summary>
/// Builds packages by hand, including hostile ones: <c>PackWriter</c> refuses to write what the reader would
/// reject, so the attacks have to be made from raw zip entries.
/// </summary>
public static class PackCrafter
{
    /// <summary>A raw zip entry. <paramref name="UnixMode" /> sets the high half of the external attributes.</summary>
    public sealed record Entry(string Name, byte[] Content, int? UnixMode = null);

    public static PackMod Mod(string guid = "com.fixture.clientmod", string version = "1.4.2", int? forgeId = 7, string digest = "")
    {
        return new PackMod
        {
            Guid = guid,
            ForgeId = forgeId,
            Name = guid,
            Version = version,
            Kind = ModKind.Client,
            ContentSha256 = digest,
        };
    }

    public static PackConfig Config(string modGuid, string path, string text)
    {
        return new PackConfig
        {
            ModGuid = modGuid,
            Path = path,
            Sha256 = Sha(Encoding.UTF8.GetBytes(text)),
        };
    }

    /// <summary>A valid manifest with the share code filled in.</summary>
    public static PackManifest Manifest(
        IEnumerable<PackMod>? mods = null,
        IEnumerable<PackConfig>? configs = null,
        IEnumerable<PackAddon>? addons = null,
        string sptVersion = "4.1.6"
    )
    {
        var manifest = new PackManifest
        {
            FormatVersion = PackFormat.CurrentVersion,
            CreatedAt = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero),
            AppVersion = "0.0.0-test",
            InstanceName = "crafted",
            SptVersion = sptVersion,
            TarkovVersion = "0.16.9.40743",
            Layout = InstanceLayout.Spt41Plus,
            Mods = [.. mods ?? [Mod()]],
            Addons = [.. addons ?? []],
            Configs = [.. configs ?? []],
        };

        return manifest with
        {
            ShareCode = PackManifestSerializer.ComputeShareCode(manifest),
        };
    }

    public static Entry ManifestEntry(PackManifest manifest)
    {
        return new Entry(PackFormat.ManifestEntry, PackManifestSerializer.Serialize(manifest));
    }

    public static Entry ConfigEntry(string path, string text)
    {
        return new Entry(PackFormat.ConfigsDirectory + path, Encoding.UTF8.GetBytes(text));
    }

    public static string Zip(string path, params Entry[] entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var entry in entries)
        {
            var created = archive.CreateEntry(entry.Name, CompressionLevel.Optimal);
            if (entry.UnixMode is { } mode)
            {
                created.ExternalAttributes = mode << 16;
            }

            using var content = created.Open();
            content.Write(entry.Content, 0, entry.Content.Length);
        }

        return path;
    }

    public static string Sha(byte[] content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }
}
