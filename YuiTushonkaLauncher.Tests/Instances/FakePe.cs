using System.Collections.Generic;
using System.Text;

namespace YuiTushonkaLauncher.Tests.Instances;

/// <summary>
/// The smallest byte sequence <c>PeVersionReader</c> accepts as a version resource: the reader looks for
/// <c>VS_VERSION_INFO</c> and the <c>FileVersion</c> key, so a fixture needs exactly those two strings.
/// </summary>
public static class FakePe
{
    /// <summary>An executable image whose file version reads back as <paramref name="version" />.</summary>
    public static byte[] WithVersion(string version)
    {
        var builder = new List<byte>();
        builder.AddRange(Encoding.Unicode.GetBytes("VS_VERSION_INFO"));
        builder.AddRange([0, 0]);
        builder.AddRange(Encoding.Unicode.GetBytes("FileVersion\0"));
        while (builder.Count % 4 != 0)
        {
            builder.Add(0);
        }

        builder.AddRange(Encoding.Unicode.GetBytes(version + "\0"));
        return [.. builder];
    }
}
