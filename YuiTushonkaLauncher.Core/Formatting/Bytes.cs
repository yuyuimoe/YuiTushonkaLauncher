using System.Globalization;

namespace YuiTushonkaLauncher.Core.Formatting;

/// <summary>Sizes for people: <c>1.2 GB</c>, <c>812 KiB</c>. Binary units, because that is what a disk shows.</summary>
public static class Bytes
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB"];

    /// <summary>Formats a byte count with at most one decimal above KiB.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
            1 => string.Create(CultureInfo.InvariantCulture, $"{value:F0} {Units[unit]}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{value:F1} {Units[unit]}"),
        };
    }
}
