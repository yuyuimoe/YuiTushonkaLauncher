using System.Text;
using System.Text.RegularExpressions;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Reads the <c>FileVersion</c> string out of a Windows executable's version resource.
/// </summary>
/// <remarks>
/// <see cref="System.Diagnostics.FileVersionInfo" /> only works on Windows, and the Tarkov build is
/// needed on Linux too, so the resource is scanned directly: locate the UTF-16 <c>FileVersion</c> key,
/// align to the next 4-byte boundary, then read the length-prefixed value that follows. If the layout
/// is anything unexpected the reader gives up and the version stays unknown — never a wrong value.
/// </remarks>
internal static partial class PeVersionReader
{
    private const int MaxScanBytes = 32 * 1024 * 1024;

    /// <summary>Reads <c>FileVersion</c> from a PE file, or <c>null</c> when it cannot be read.</summary>
    public static string? TryReadFileVersion(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaxScanBytes)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return TryReadFileVersion(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads <c>FileVersion</c> from an in-memory PE image.</summary>
    public static string? TryReadFileVersion(ReadOnlySpan<byte> image)
    {
        var key = Encoding.Unicode.GetBytes("FileVersion\0");
        var index = image.IndexOf(key);
        if (index < 0)
        {
            return null;
        }

        // The value follows the key, padded to a 4-byte boundary, as a null-terminated UTF-16 string.
        var cursor = (index + key.Length + 3) & ~3;
        if (cursor >= image.Length)
        {
            return null;
        }

        var builder = new StringBuilder(16);
        while (cursor + 1 < image.Length && builder.Length < 64)
        {
            var character = (char)BitConverter.ToUInt16(image.Slice(cursor, 2));
            if (character == '\0')
            {
                break;
            }

            builder.Append(character);
            cursor += 2;
        }

        var value = builder.ToString().Trim();
        return VersionPattern().IsMatch(value) ? value : null;
    }

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+){1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
