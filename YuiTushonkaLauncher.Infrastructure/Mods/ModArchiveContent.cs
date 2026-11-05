using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using YuiTushonkaLauncher.Core.Mods;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// A mod archive opened without caring which format it is. The Forge serves zip, 7z and rar, and the download URL
/// says nothing: it ends in the version (<c>/mod/download/902/bigbrain/1.5.0</c>), so the name is no evidence at all
/// and the format has to be read from the first bytes.
/// </summary>
/// <remarks>
/// Solid archives are read through a forward-only reader, because asking a solid 7z for one entry decompresses from
/// the start every time; the inspector never opens streams, so it stays cheap for all three formats.
/// </remarks>
internal sealed class ModArchiveContent : IDisposable
{
    private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] Rar4Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00];
    private static readonly byte[] Rar5Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    private readonly IArchive _archive;

    private ModArchiveContent(string path, ModArchiveFormat format, IArchive archive)
    {
        ArchivePath = path;
        Format = format;
        _archive = archive;
    }

    /// <summary>The archive's path.</summary>
    public string ArchivePath { get; }

    /// <summary>What the first bytes said it is.</summary>
    public ModArchiveFormat Format { get; }

    /// <summary>Every entry, names and sizes only — nothing is decompressed.</summary>
    public IReadOnlyList<IArchiveEntry> Entries
    {
        get { return [.. _archive.Entries]; }
    }

    /// <summary>True when reading one entry costs a decompression from the start of the archive.</summary>
    public bool IsSolid
    {
        get { return _archive.IsSolid; }
    }

    /// <summary>Opens a file that says which container it is. Refuses anything else, by name.</summary>
    public static ModArchiveContent Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new ModArchiveException($"The archive '{Path.GetFileName(path)}' is not there any more.", path);
        }

        var format = Detect(path);
        if (format == ModArchiveFormat.Unknown)
        {
            throw new ModArchiveException(
                $"'{Path.GetFileName(path)}' is not a zip, 7z or rar archive; the launcher cannot install it.",
                path
            );
        }

        try
        {
            // The factory asks the bytes, not the extension, so a mislabelled file still opens.
            return new ModArchiveContent(path, format, ArchiveFactory.Open(path, new ReaderOptions { LeaveStreamOpen = false }));
        }
        catch (Exception exception) when (exception is not ModArchiveException)
        {
            throw new ModArchiveException(
                $"'{Path.GetFileName(path)}' could not be opened as a {Describe(format)} archive: {exception.Message}",
                path
            );
        }
    }

    /// <summary>Reads the first bytes of a file and says which container it is.</summary>
    public static ModArchiveFormat Detect(string path)
    {
        Span<byte> head = stackalloc byte[8];
        int read;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (IOException)
        {
            return ModArchiveFormat.Unknown;
        }

        var bytes = head[..read];

        // A zip starts with a local file header, an empty archive, or a spanned marker.
        if (bytes.Length >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B)
        {
            return ModArchiveFormat.Zip;
        }

        if (bytes.StartsWith(SevenZipSignature))
        {
            return ModArchiveFormat.SevenZip;
        }

        return bytes.StartsWith(Rar4Signature) || bytes.StartsWith(Rar5Signature) ? ModArchiveFormat.Rar : ModArchiveFormat.Unknown;
    }

    /// <summary>
    /// True when entries can be opened by name. A solid archive cannot: asking for the last entry means decompressing
    /// everything before it, so those are read in one forward pass instead.
    /// </summary>
    public bool CanOpenEntriesByName
    {
        get { return !_archive.IsSolid; }
    }

    /// <summary>A forward-only reader, for writing a solid archive out in one pass.</summary>
    public IReader CreateReader()
    {
        return _archive.ExtractAllEntries();
    }

    /// <summary>Opens one entry by name. Only valid when <see cref="CanOpenEntriesByName" />.</summary>
    public Stream OpenEntry(IArchiveEntry entry)
    {
        return entry.OpenEntryStream();
    }

    /// <summary>The archive as a sentence, for messages.</summary>
    public static string Describe(ModArchiveFormat format)
    {
        return format switch
        {
            ModArchiveFormat.Zip => "zip",
            ModArchiveFormat.SevenZip => "7z",
            ModArchiveFormat.Rar => "rar",
            _ => "unknown",
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _archive.Dispose();
    }
}
