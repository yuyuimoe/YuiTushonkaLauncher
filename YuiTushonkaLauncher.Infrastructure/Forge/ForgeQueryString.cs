using System.Text;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// Builds query strings for the API. Filters use Laravel's <c>filter[name]</c> syntax — this is the
/// only shape the live API accepts (<c>?guids=</c> and <c>?ids=</c> are silently ignored).
/// </summary>
internal sealed class ForgeQueryString
{
    private readonly List<string> _parts = [];

    /// <summary>Adds a plain parameter; <c>null</c> or empty values are skipped.</summary>
    public ForgeQueryString Add(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        return this;
    }

    /// <summary>Adds an integer parameter when it has a value.</summary>
    public ForgeQueryString Add(string key, int? value)
    {
        return value is null ? this : Add(key, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Adds a boolean parameter when it has a value.</summary>
    public ForgeQueryString Add(string key, bool? value)
    {
        return value is null ? this : Add(key, value.Value ? "true" : "false");
    }

    /// <summary>Adds <c>filter[...]</c>, joining lists with commas.</summary>
    public ForgeQueryString AddFilter(string name, IEnumerable<string> values)
    {
        var joined = string.Join(',', values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return Add($"filter[{name}]", joined);
    }

    /// <summary>Adds <c>filter[...]</c> for a single value.</summary>
    public ForgeQueryString AddFilter(string name, string? value)
    {
        return Add($"filter[{name}]", value);
    }

    /// <summary>Adds <c>filter[...]</c> for an integer value.</summary>
    public ForgeQueryString AddFilter(string name, int? value)
    {
        return Add($"filter[{name}]", value);
    }

    /// <summary>Adds <c>filter[...]</c> for a boolean value.</summary>
    public ForgeQueryString AddFilter(string name, bool? value)
    {
        return Add($"filter[{name}]", value);
    }

    /// <summary>The query string, including the leading <c>?</c>, or an empty string.</summary>
    public override string ToString()
    {
        if (_parts.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(_parts.Count * 24);
        builder.Append('?').Append(string.Join('&', _parts));
        return builder.ToString();
    }
}
