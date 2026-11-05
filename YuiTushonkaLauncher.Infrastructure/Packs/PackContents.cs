using YuiTushonkaLauncher.Core.Packs;

namespace YuiTushonkaLauncher.Infrastructure.Packs;

/// <summary>A package that passed every check: the manifest and the config files it declares.</summary>
public sealed record PackContents
{
    public required PackManifest Manifest { get; init; }

    /// <summary>Config bytes by instance-relative path, already verified against the manifest's hashes.</summary>
    public required IReadOnlyDictionary<string, byte[]> Configs { get; init; }
}
