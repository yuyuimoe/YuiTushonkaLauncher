using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>What one entry of a mod archive is.</summary>
public enum ArchiveEntryKind
{
    /// <summary>A BepInEx plugin assembly (<c>BepInEx/plugins/**</c>).</summary>
    ClientAssembly,

    /// <summary>A server mod assembly (<c>SPT_Runtime/user/mods/**</c>).</summary>
    ServerAssembly,

    /// <summary>Extra game assemblies some mods drop into <c>EscapeFromTarkov_Data/Managed</c>.</summary>
    GameAssembly,

    /// <summary>Configuration the mod ships (copied into the instance).</summary>
    Config,

    /// <summary>Documentation, images and other files that still belong to the mod.</summary>
    Content,

    /// <summary>Path traversal, absolute paths or links: the archive is hostile.</summary>
    Unsafe,
}

/// <summary>One inspected entry.</summary>
public sealed record ArchiveEntry
{
    public required string RelativePath { get; init; }

    public required ArchiveEntryKind Kind { get; init; }

    public long Length { get; init; }

    /// <summary>True when the path needs its instance location rewritten (mod root stripped).</summary>
    public bool NeedsRelocation { get; init; }
}

/// <summary>Result of inspecting an archive without extracting it.</summary>
public sealed record ArchiveInspection
{
    public required string ArchivePath { get; init; }

    public IReadOnlyList<ArchiveEntry> Entries { get; init; } = [];

    /// <summary>
    /// Leading directory shared by every entry (a wrapper folder such as <c>MyMod-1.2.3/</c>), or
    /// <c>null</c> when the archive is already rooted at the instance layout.
    /// </summary>
    public string? RootPrefix { get; init; }

    /// <summary>The mods kind(s) the archive ships.</summary>
    public ModKind Kind { get; init; }

    /// <summary>Assemblies worth identifying, most specific first.</summary>
    public IReadOnlyList<string> CandidateAssemblies { get; init; } = [];

    /// <summary>Entries whose path tried to escape the destination.</summary>
    public IReadOnlyList<string> UnsafeEntries { get; init; } = [];

    /// <summary>True when the archive carries already-built binaries the launcher must handle.</summary>
    public bool ShipsBinaries
    {
        get
        {
            return Entries.Any(entry =>
                entry.Kind is ArchiveEntryKind.ClientAssembly or ArchiveEntryKind.ServerAssembly or ArchiveEntryKind.GameAssembly
            );
        }
    }

    /// <summary>Raised when the archive ship binaries (see the threat model in phase 8).</summary>
    public bool IsUnsafe
    {
        get { return UnsafeEntries.Count > 0; }
    }
}

/// <summary>Inspects a mod archive's contents without writing anything to disk.</summary>
public interface IArchiveInspector
{
    /// <summary>Lists and classifies the entries of <paramref name="archivePath" />.</summary>
    ArchiveInspection Inspect(string archivePath);
}

/// <summary>Extracts an archive, refusing anything that would escape the destination.</summary>
public interface IArchiveExtractor
{
    /// <summary>Extracts into <paramref name="destinationDirectory" />, returning the number of files written.</summary>
    /// <exception cref="Core.Files.FileLinkException">An entry tried to escape the destination.</exception>
    Task<int> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default);
}
