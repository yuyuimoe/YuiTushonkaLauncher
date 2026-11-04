using System.Globalization;

namespace YuiTushonkaLauncher.Core.Domain;

/// <summary>
/// A semantic version (<see href="https://semver.org" />). Used for SPT versions and for mod
/// versions, which SPT itself validates as semver.
/// </summary>
/// <remarks>
/// Parsing is deliberately lenient about the number of parts (<c>1</c>, <c>1.2</c> and <c>1.2.3</c>
/// are accepted, missing parts default to zero) because Forge data can be messy, but the comparison
/// follows semver: a pre-release sorts before its release.
/// </remarks>
public readonly record struct SemanticVersion : IComparable<SemanticVersion>
{
    public SemanticVersion(int major, int minor = 0, int patch = 0, string? preRelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
    }

    /// <summary>Major component.</summary>
    public int Major { get; }

    /// <summary>Minor component.</summary>
    public int Minor { get; }

    /// <summary>Patch component.</summary>
    public int Patch { get; }

    /// <summary>Pre-release identifier, without the leading hyphen. <c>null</c> for a release.</summary>
    public string? PreRelease { get; }

    /// <summary>Parses <c>X.Y.Z[-pre]</c>, tolerating surrounding whitespace and missing parts.</summary>
    public static SemanticVersion Parse(string value)
    {
        if (TryParse(value, out var version))
        {
            return version;
        }

        throw new FormatException($"'{value}' is not a valid semantic version.");
    }

    /// <summary>Attempts to parse <c>X.Y.Z[-pre]</c>.</summary>
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        string? preRelease = null;

        var hyphen = text.IndexOf('-');
        if (hyphen >= 0)
        {
            preRelease = text[(hyphen + 1)..];
            text = text[..hyphen];

            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length is 0 or > 3)
        {
            return false;
        }

        Span<int> numbers = stackalloc int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SemanticVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0)
        {
            return comparison;
        }

        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    /// <summary>True when this is a pre-release of <paramref name="release" /> (same numbers, has suffix).</summary>
    public bool IsPreReleaseOf(SemanticVersion release)
    {
        return PreRelease is not null && Major == release.Major && Minor == release.Minor && Patch == release.Patch;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) >= 0;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        return PreRelease is null ? core : $"{core}-{PreRelease}";
    }
}
