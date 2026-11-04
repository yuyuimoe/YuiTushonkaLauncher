using System.Text.Json;

namespace YuiTushonkaLauncher.Core.Localization;

/// <summary>One language the launcher ships, as the settings screen shows it.</summary>
/// <param name="Code">Culture name, e.g. <c>en</c> or <c>pt-BR</c>.</param>
/// <param name="NativeName">The language's own name, so a user who cannot read English can still find it.</param>
public sealed record LocaleLanguage(string Code, string NativeName);

/// <summary>
/// A language's strings. The file shape is SPT's: nested objects, so a locale from SPT's <c>Locales/</c> folder can be
/// dropped in unchanged. Nested keys are flattened to dotted ones (<c>instances.title</c>) because a flat table is what
/// a lookup wants and what a "both files have the same keys" test can compare.
/// </summary>
public static class LocaleBundle
{
    /// <summary>The language every other one falls back to.</summary>
    public const string FallbackLanguage = "en";

    /// <summary>
    /// Flattens a locale document. Values that are not strings are ignored rather than failing: SPT locales carry
    /// numbers and arrays too, and a launcher that cannot use them should not refuse to start.
    /// </summary>
    /// <exception cref="JsonException">The document is not a JSON object.</exception>
    public static IReadOnlyDictionary<string, string> Flatten(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A locale file must be a JSON object.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        FlattenInto(root: null, document.RootElement, result);
        return result;
    }

    /// <summary>
    /// Fills the gaps of <paramref name="language" /> from <paramref name="fallback" />. A half-translated locale is
    /// normal while a translation is in progress; showing English in the gaps beats showing a key.
    /// </summary>
    public static IReadOnlyDictionary<string, string> WithFallback(
        IReadOnlyDictionary<string, string> language,
        IReadOnlyDictionary<string, string> fallback
    )
    {
        var result = new Dictionary<string, string>(fallback, StringComparer.Ordinal);
        foreach (var (key, value) in language)
        {
            result[key] = value;
        }

        return result;
    }

    /// <summary>Keys present in one table and missing from the other, for the translation completeness test.</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        return
        [
            .. left
                .Keys.Except(right.Keys, StringComparer.Ordinal)
                .Concat(right.Keys.Except(left.Keys, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Placeholders a value declares, e.g. <c>{0}</c> and <c>{name}</c>, sorted.</summary>
    public static IReadOnlyList<string> Placeholders(string value)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var index = 0;
        while (index < value.Length)
        {
            var open = value.IndexOf('{', index);
            if (open < 0)
            {
                break;
            }

            var close = value.IndexOf('}', open + 1);
            if (close < 0)
            {
                break;
            }

            var inside = value[(open + 1)..close];
            if (inside.Length > 0 && !inside.Contains('{'))
            {
                // Drop the alignment/format tail: "{0,-10:F2}" declares the same placeholder as "{0}".
                var colon = inside.IndexOfAny([',', ':']);
                found.Add("{" + (colon < 0 ? inside : inside[..colon]) + "}");
            }

            index = close + 1;
        }

        return [.. found];
    }

    private static void FlattenInto(string? root, JsonElement element, Dictionary<string, string> result)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = root is null ? property.Name : root + "." + property.Name;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    FlattenInto(key, property.Value, result);
                    break;
                case JsonValueKind.String:
                    result[key] = property.Value.GetString() ?? string.Empty;
                    break;
            }
        }
    }
}
