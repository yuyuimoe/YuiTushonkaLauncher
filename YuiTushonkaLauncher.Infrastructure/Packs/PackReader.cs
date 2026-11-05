using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Packs;

namespace YuiTushonkaLauncher.Infrastructure.Packs;

/// <summary>
/// Opens a <c>.sptpack</c> and returns it only if every check passes. A package is untrusted input:
/// it can name files, so nothing is written, and nothing is returned, until the whole archive has been
/// validated.
/// </summary>
/// <remarks>
/// <para>
/// What is checked, in order: entry count; every entry name (zip-slip, absolute paths, backslashes,
/// Windows device names and trailing dots, symlinks, duplicates); forbidden extensions before anything
/// else so a binary gets its own error; uncompressed sizes counted while reading (the sizes in the zip
/// header are attacker-controlled); the format version <em>before</em> the schema, so a newer format is
/// reported as such; the schema; the share code; and finally each config's content against the hash the
/// manifest declares.
/// </para>
/// <para>
/// The share code and the hashes catch corruption and accidental edits. They are not a signature: someone
/// who builds a package can compute them too. That is why the import never takes a URL or a binary from
/// the package and shows a review screen first.
/// </para>
/// </remarks>
public sealed partial class PackReader(ILogger<PackReader> logger)
{
    /// <summary>Reads and validates <paramref name="packagePath" />.</summary>
    /// <exception cref="PackException">The package is malformed or unsafe.</exception>
    public PackContents Read(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new PackException(PackProblem.Malformed, $"The package '{packagePath}' does not exist.");
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(packagePath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new PackException(
                PackProblem.Malformed,
                $"'{Path.GetFileName(packagePath)}' is not a valid package: {exception.Message}"
            );
        }

        using (archive)
        {
            var contents = ReadArchive(archive);
            logger.LogInformation(
                "Read package {Package}: {Mods} mod(s), {Addons} addon(s), {Configs} config(s), share code {Code}",
                Path.GetFileName(packagePath),
                contents.Manifest.Mods.Count,
                contents.Manifest.Addons.Count,
                contents.Configs.Count,
                contents.Manifest.ShareCode
            );

            return contents;
        }
    }

    private static PackContents ReadArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > PackRules.MaxEntries)
        {
            throw new PackException(
                PackProblem.TooLarge,
                $"The package holds {archive.Entries.Count} entries; the limit is {PackRules.MaxEntries}."
            );
        }

        byte[]? manifestBytes = null;
        var configs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            var kind = Classify(entry);
            if (kind == EntryKind.Ignored)
            {
                continue;
            }

            if (!seen.Add(entry.FullName))
            {
                throw new PackException(PackProblem.UnsafeEntry, $"The package has two entries named '{entry.FullName}' (ignoring case).");
            }

            var limit = kind == EntryKind.Manifest ? PackRules.MaxManifestBytes : PackRules.MaxConfigBytes;
            var bytes = ReadBounded(entry, limit, ref total);

            switch (kind)
            {
                case EntryKind.Manifest:
                    manifestBytes = bytes;
                    break;
                case EntryKind.Config:
                    configs[entry.FullName[PackFormat.ConfigsDirectory.Length..]] = bytes;
                    break;
            }
        }

        if (manifestBytes is null)
        {
            throw new PackException(PackProblem.Malformed, $"The package has no {PackFormat.ManifestEntry}.");
        }

        var manifest = ParseManifest(manifestBytes);
        Validate(manifest, configs);

        return new PackContents { Manifest = manifest, Configs = configs };
    }

    private static EntryKind Classify(ZipArchiveEntry entry)
    {
        var name = entry.FullName;

        if (name.Contains('\\') || name.Contains('\0') || name.StartsWith('/'))
        {
            throw new PackException(PackProblem.UnsafeEntry, $"The entry '{name}' has an unsafe name.");
        }

        // A binary is refused by name first, wherever it sits, so the error says what it is.
        if (PackRules.IsForbiddenExtension(name))
        {
            throw new PackException(
                PackProblem.ForbiddenFile,
                $"The package contains '{name}', an executable or binary file. Packages never carry binaries."
            );
        }

        if (IsSymbolicLink(entry))
        {
            throw new PackException(PackProblem.UnsafeEntry, $"The entry '{name}' is a symbolic link.");
        }

        if (name.EndsWith('/'))
        {
            var directory = name.TrimEnd('/');
            if (
                directory == "configs"
                || (directory.StartsWith("configs/", StringComparison.Ordinal) && PackRules.IsSafeRelativePath(directory))
            )
            {
                return EntryKind.Ignored;
            }

            throw new PackException(PackProblem.UnsafeEntry, $"The directory entry '{name}' is not allowed.");
        }

        if (name == PackFormat.ManifestEntry)
        {
            return EntryKind.Manifest;
        }

        if (name == PackFormat.ReadmeEntry)
        {
            return EntryKind.Readme;
        }

        if (name.StartsWith(PackFormat.ConfigsDirectory, StringComparison.Ordinal))
        {
            var relative = name[PackFormat.ConfigsDirectory.Length..];
            if (!PackRules.IsSafeRelativePath(relative))
            {
                throw new PackException(PackProblem.UnsafeEntry, $"The entry '{name}' would escape the instance.");
            }

            if (!PackRules.IsAllowedConfigExtension(relative))
            {
                throw new PackException(PackProblem.UnsafeEntry, $"The entry '{name}' is not a shareable config file type.");
            }

            return EntryKind.Config;
        }

        throw new PackException(PackProblem.UnsafeEntry, $"The package contains an unexpected entry '{name}'.");
    }

    /// <summary>Unix mode bits live in the high half of the external attributes; <c>S_IFLNK</c> is <c>0xA000</c>.</summary>
    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        return ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
    }

    /// <summary>Reads an entry, counting the bytes actually produced: the declared length cannot be trusted.</summary>
    private static byte[] ReadBounded(ZipArchiveEntry entry, long limit, ref long total)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;

            if (buffer.Length + read > limit)
            {
                throw new PackException(
                    PackProblem.TooLarge,
                    $"The entry '{entry.FullName}' is larger than the {limit / 1024:N0} KiB limit."
                );
            }

            if (total > PackRules.MaxTotalBytes)
            {
                throw new PackException(PackProblem.TooLarge, $"The package expands past {PackRules.MaxTotalBytes / 1024 / 1024} MiB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static PackManifest ParseManifest(byte[] bytes)
    {
        var version = PackManifestSerializer.ReadFormatVersion(bytes);

        if (version is null or < 1)
        {
            throw new PackException(PackProblem.Malformed, "The manifest has no valid formatVersion.");
        }

        if (version > PackFormat.CurrentVersion)
        {
            throw new PackException(
                PackProblem.FormatTooNew,
                $"This package uses format {version}, but this launcher reads up to format {PackFormat.CurrentVersion}. Update the launcher to import it."
            );
        }

        try
        {
            return PackManifestSerializer.Deserialize(bytes);
        }
        catch (JsonException exception)
        {
            throw new PackException(PackProblem.Malformed, $"The manifest is not valid: {exception.Message}");
        }
    }

    private static void Validate(PackManifest manifest, Dictionary<string, byte[]> configs)
    {
        if (
            manifest.Mods.Count > PackRules.MaxMods
            || manifest.Addons.Count > PackRules.MaxMods
            || manifest.Configs.Count > PackRules.MaxEntries
        )
        {
            throw new PackException(PackProblem.TooLarge, "The manifest lists more entries than the launcher accepts.");
        }

        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in manifest.Mods)
        {
            RequireIdentity(mod.Guid, mod.Version, "mod");
            RequireOptionalSha(mod.ContentSha256, $"mod '{mod.Guid}'");
            owners.Add(mod.Guid);
        }

        foreach (var addon in manifest.Addons)
        {
            RequireIdentity(addon.Guid, addon.Version, "addon");
            RequireOptionalSha(addon.ContentSha256, $"addon '{addon.Guid}'");

            if (!manifest.Mods.Any(mod => string.Equals(mod.Guid, addon.ParentGuid, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PackException(
                    PackProblem.Malformed,
                    $"The addon '{addon.Guid}' names a parent mod '{addon.ParentGuid}' that the package does not list."
                );
            }

            owners.Add(addon.Guid);
        }

        if (!string.Equals(manifest.ShareCode, PackManifestSerializer.ComputeShareCode(manifest), StringComparison.Ordinal))
        {
            throw new PackException(PackProblem.Tampered, "The share code does not match the package contents: it was edited or damaged.");
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var config in manifest.Configs)
        {
            ValidateConfig(config, owners, configs);
            declared.Add(config.Path);
        }

        foreach (var undeclared in configs.Keys.Where(path => !declared.Contains(path)))
        {
            throw new PackException(PackProblem.UnsafeEntry, $"The package carries '{undeclared}', which the manifest does not declare.");
        }
    }

    private static void ValidateConfig(PackConfig config, HashSet<string> owners, Dictionary<string, byte[]> configs)
    {
        if (!PackRules.IsSafeRelativePath(config.Path))
        {
            throw new PackException(PackProblem.UnsafeEntry, $"The config path '{config.Path}' would escape the instance.");
        }

        if (PackRules.IsForbiddenExtension(config.Path))
        {
            throw new PackException(PackProblem.ForbiddenFile, $"The manifest declares '{config.Path}', an executable or binary file.");
        }

        if (!PackRules.IsAllowedConfigExtension(config.Path))
        {
            throw new PackException(PackProblem.UnsafeEntry, $"'{config.Path}' is not a shareable config file type.");
        }

        if (!owners.Contains(config.ModGuid))
        {
            throw new PackException(
                PackProblem.Malformed,
                $"The config '{config.Path}' belongs to '{config.ModGuid}', which the package does not list."
            );
        }

        if (!Sha256Pattern().IsMatch(config.Sha256))
        {
            throw new PackException(PackProblem.Malformed, $"The config '{config.Path}' has no valid sha256.");
        }

        if (!configs.TryGetValue(config.Path, out var content))
        {
            throw new PackException(PackProblem.Malformed, $"The manifest declares '{config.Path}' but the package has no such entry.");
        }

        var actual = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(actual, config.Sha256, StringComparison.Ordinal))
        {
            throw new PackException(PackProblem.Tampered, $"The content of '{config.Path}' does not match the hash in the manifest.");
        }
    }

    private static void RequireIdentity(string guid, string version, string what)
    {
        if (string.IsNullOrWhiteSpace(guid) || guid.Length > 200)
        {
            throw new PackException(PackProblem.Malformed, $"A {what} has no valid guid.");
        }

        if (!SemanticVersion.TryParse(version, out _))
        {
            throw new PackException(PackProblem.Malformed, $"The {what} '{guid}' has an unreadable version '{version}'.");
        }
    }

    private static void RequireOptionalSha(string value, string owner)
    {
        if (value.Length > 0 && !Sha256Pattern().IsMatch(value))
        {
            throw new PackException(PackProblem.Malformed, $"The {owner} has an invalid contentSha256.");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    private enum EntryKind
    {
        Ignored,

        Manifest,

        Readme,

        Config,
    }
}
