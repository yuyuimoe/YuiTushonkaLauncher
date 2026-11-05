namespace YuiTushonkaLauncher.Core.Domain;

/// <summary>
/// A version constraint, as published by the Forge API (<c>spt_version_constraint</c>,
/// <c>mod_version_constraint</c>) and by <c>IModMetadata.SptVersion</c>/<c>ModDependencies</c>.
/// </summary>
/// <remarks>
/// Supports the shapes that actually appear in the ecosystem: bare versions, <c>^</c>, <c>~</c>,
/// comparison operators, comma- or space-separated conjunctions, NuGet-style bracket ranges and
/// <c>||</c> alternatives. An empty constraint means "any version".
/// </remarks>
public sealed class VersionRange : IEquatable<VersionRange>
{
    private readonly Constraint[] _alternatives;
    private readonly string _raw;

    private VersionRange(string raw, Constraint[] alternatives)
    {
        _raw = raw;
        _alternatives = alternatives;
    }

    /// <summary>Matches every version.</summary>
    public static VersionRange Any { get; } = new("*", [new Constraint(_ => true)]);

    /// <summary>The constraint text this instance was parsed from.</summary>
    public string Raw
    {
        get { return _raw; }
    }

    /// <summary>Parses a constraint. Throws <see cref="FormatException" /> when it cannot be understood.</summary>
    public static VersionRange Parse(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint) || constraint.Trim() == "*")
        {
            return Any;
        }

        var raw = constraint.Trim();
        var alternatives = raw.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<Constraint>(alternatives.Length);

        foreach (var alternative in alternatives)
        {
            parsed.Add(ParseAlternative(alternative));
        }

        if (parsed.Count == 0)
        {
            throw new FormatException($"'{constraint}' is not a valid version range.");
        }

        return new VersionRange(raw, [.. parsed]);
    }

    /// <summary>Parses a constraint, returning <c>false</c> instead of throwing.</summary>
    public static bool TryParse(string? constraint, out VersionRange range)
    {
        try
        {
            range = Parse(constraint);
            return true;
        }
        catch (FormatException)
        {
            range = Any;
            return false;
        }
    }

    /// <summary>True when <paramref name="version" /> satisfies the constraint.</summary>
    public bool Satisfies(SemanticVersion version)
    {
        foreach (var alternative in _alternatives)
        {
            if (alternative.Matches(version))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool Equals(VersionRange? other)
    {
        return other is not null && string.Equals(_raw, other._raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is VersionRange other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(_raw);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return _raw;
    }

    private static Constraint ParseAlternative(string alternative)
    {
        var text = alternative.Trim();

        if (text.StartsWith('[') || text.StartsWith('('))
        {
            return ParseBracketRange(text);
        }

        var comparators = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var predicates = new List<Func<SemanticVersion, bool>>(comparators.Length);

        foreach (var comparator in comparators)
        {
            predicates.Add(ParseComparator(comparator));
        }

        return new Constraint(version =>
        {
            foreach (var predicate in predicates)
            {
                if (!predicate(version))
                {
                    return false;
                }
            }

            return true;
        });
    }

    private static Func<SemanticVersion, bool> ParseComparator(string comparator)
    {
        if (comparator.StartsWith(">=", StringComparison.Ordinal))
        {
            var bound = SemanticVersion.Parse(comparator[2..]);
            return version => version >= bound;
        }

        if (comparator.StartsWith("<=", StringComparison.Ordinal))
        {
            var bound = SemanticVersion.Parse(comparator[2..]);
            return version => version <= bound;
        }

        if (comparator.StartsWith('>'))
        {
            var bound = SemanticVersion.Parse(comparator[1..]);
            return version => version > bound;
        }

        if (comparator.StartsWith('<'))
        {
            var bound = SemanticVersion.Parse(comparator[1..]);
            return version => version < bound;
        }

        if (comparator.StartsWith('^'))
        {
            var from = SemanticVersion.Parse(comparator[1..]);
            var to = from.Major > 0 ? new SemanticVersion(from.Major + 1, 0, 0) : new SemanticVersion(0, from.Minor + 1, 0);
            return version => version >= from && version < to;
        }

        if (comparator.StartsWith('~'))
        {
            var from = SemanticVersion.Parse(comparator[1..]);
            var to = new SemanticVersion(from.Major, from.Minor + 1, 0);
            return version => version >= from && version < to;
        }

        var exact = SemanticVersion.Parse(comparator.TrimStart('='));
        return version => version == exact;
    }

    private static Constraint ParseBracketRange(string text)
    {
        var lowerInclusive = text[0] == '[';
        var upperInclusive = text[^1] == ']';
        var body = text[1..^1];

        var parts = body.Split(',');
        if (parts.Length is < 1 or > 2)
        {
            throw new FormatException($"'{text}' is not a valid version range.");
        }

        // "[1.0.0]" is an exact version.
        if (parts.Length == 1)
        {
            var exact = SemanticVersion.Parse(parts[0]);
            return new Constraint(version => version == exact);
        }

        var lower = string.IsNullOrWhiteSpace(parts[0]) ? (SemanticVersion?)null : SemanticVersion.Parse(parts[0]);
        var upper = string.IsNullOrWhiteSpace(parts[1]) ? (SemanticVersion?)null : SemanticVersion.Parse(parts[1]);

        return new Constraint(version =>
        {
            if (lower is { } min)
            {
                var aboveLower = lowerInclusive ? version >= min : version > min;
                if (!aboveLower)
                {
                    return false;
                }
            }

            if (upper is { } max)
            {
                var belowUpper = upperInclusive ? version <= max : version < max;
                if (!belowUpper)
                {
                    return false;
                }
            }

            return true;
        });
    }

    private sealed class Constraint(Func<SemanticVersion, bool> matches)
    {
        public bool Matches(SemanticVersion version)
        {
            return matches(version);
        }
    }
}
