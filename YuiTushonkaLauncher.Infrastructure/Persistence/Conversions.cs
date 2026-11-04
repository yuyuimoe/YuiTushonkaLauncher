using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>Value converters shared by the model configuration.</summary>
internal static class Conversions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Semantic version stored as its canonical string form.</summary>
    public static readonly ValueConverter<SemanticVersion, string> SemanticVersionConverter = new(
        version => version.ToString(),
        text => SemanticVersion.Parse(text)
    );

    /// <summary>Version constraint stored as the text it was parsed from.</summary>
    public static readonly ValueConverter<VersionRange, string> VersionRangeConverter = new(
        range => range.Raw,
        text => VersionRange.Parse(text)
    );

    /// <summary>Change tracking for <see cref="VersionRange" />, which is a reference type.</summary>
    public static readonly ValueComparer<VersionRange> VersionRangeValueComparer = new(
        (left, right) => left!.Raw == right!.Raw,
        range => StringComparer.Ordinal.GetHashCode(range.Raw),
        range => VersionRange.Parse(range.Raw)
    );

    /// <summary>Environment variable maps stored as a JSON object.</summary>
    public static readonly ValueConverter<Dictionary<string, string>, string> StringMapConverter = new(
        map => JsonSerializer.Serialize(map, JsonOptions),
        json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>()
    );

    /// <summary>Change tracking for the environment map.</summary>
    public static readonly ValueComparer<Dictionary<string, string>> StringMapValueComparer = new(
        (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
        map => JsonSerializer.Serialize(map, JsonOptions).GetHashCode(StringComparison.Ordinal),
        map => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(map, JsonOptions), JsonOptions)!
    );
}
