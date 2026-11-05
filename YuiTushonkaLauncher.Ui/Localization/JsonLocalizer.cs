using System.Globalization;
using System.Reflection;
using System.Text.Json;
using YuiTushonkaLauncher.Core.Localization;

namespace YuiTushonkaLauncher.Localization;

/// <summary>
/// Reads the locale files embedded in this assembly. Embedded rather than loaded from disk: a single-file build has no
/// asset folder to read, and the headless tests have no Avalonia asset loader.
/// </summary>
public sealed class JsonLocalizer : ILocalizer
{
    /// <summary>The shipped languages, in the order the settings screen lists them.</summary>
    public static readonly IReadOnlyList<LocaleLanguage> Languages = [new("en", "English"), new("pt-BR", "Português (Brasil)")];

    private readonly Assembly _assembly;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _table = new Dictionary<string, string>();

    public JsonLocalizer(string language)
    {
        _assembly = typeof(JsonLocalizer).Assembly;
        Language = LocaleBundle.FallbackLanguage;
        SetLanguage(language);
    }

    public string Language { get; private set; }

    public IReadOnlyList<LocaleLanguage> AvailableLanguages
    {
        get { return Languages; }
    }

    public event EventHandler? LanguageChanged;

    public IReadOnlyCollection<string> MissingKeys
    {
        get { return [.. _missing.Order(StringComparer.Ordinal)]; }
    }

    public string this[string key]
    {
        get
        {
            if (_table.TryGetValue(key, out var value))
            {
                return value;
            }

            if (_missing.Add(key))
            {
                // Log messages stay English (they are diagnostics), so this is the only place a missing key is noted.
                Serilog.Log.Debug("Missing locale key {Key} for {Language}", key, Language);
            }

            return key;
        }
    }

    /// <summary>The resource name a locale file must have to be found.</summary>
    public static string ResourceName(string language)
    {
        return "YuiTushonkaLauncher.Assets.Locales." + language + ".json";
    }

    public string Format(string key, params object?[] arguments)
    {
        var format = this[key];
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, arguments);
        }
        catch (FormatException)
        {
            // A translated string with a broken placeholder must not bring the window down; show it as written.
            return format;
        }
    }

    public void SetLanguage(string language)
    {
        var code = Normalise(language);
        if (string.Equals(code, Language, StringComparison.OrdinalIgnoreCase) && _table.Count > 0)
        {
            return;
        }

        Language = code;
        _table = LocaleBundle.WithFallback(Read(code), Read(LocaleBundle.FallbackLanguage));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Maps a culture name onto a shipped one: <c>pt-BR</c> and <c>pt</c> both load Portuguese.</summary>
    public static string Resolve(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return FallbackOrDefault();
        }

        var exact = Tolerate().FirstOrDefault(candidate => string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var language = requested.Split('-', '_')[0];
        return Tolerate()
                .FirstOrDefault(candidate =>
                    candidate.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate, language, StringComparison.OrdinalIgnoreCase)
                )
            ?? FallbackOrDefault();
    }

    private static IEnumerable<string> Tolerate()
    {
        return Languages.Select(language => language.Code);
    }

    private static string FallbackOrDefault()
    {
        return LocaleBundle.FallbackLanguage;
    }

    private static string Normalise(string? language)
    {
        return Resolve(language);
    }

    private IReadOnlyDictionary<string, string> Read(string language)
    {
        if (_loaded.TryGetValue(language, out var cached))
        {
            return cached;
        }

        using var stream = _assembly.GetManifestResourceStream(ResourceName(language));
        if (stream is null)
        {
            _loaded[language] = new Dictionary<string, string>();
            return _loaded[language];
        }

        using var reader = new StreamReader(stream);
        var table = LocaleBundle.Flatten(reader.ReadToEnd());
        _loaded[language] = table;
        return table;
    }
}
