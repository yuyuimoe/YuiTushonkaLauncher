namespace YuiTushonkaLauncher.Core.Localization;

/// <summary>
/// Key-based string lookup, so no user-facing text is a literal in XAML or in a view model. Log messages stay English
/// and untranslated: they are diagnostics for a bug report, not UI ([D11](../../docs/plan/04-decisions.md#d11-i18n-and-ui-tests-from-day-one)).
/// </summary>
public interface ILocalizer
{
    /// <summary>Culture name currently used, e.g. <c>pt-BR</c>.</summary>
    string Language { get; }

    /// <summary>Every shipped language, for the settings screen.</summary>
    IReadOnlyList<LocaleLanguage> AvailableLanguages { get; }

    /// <summary>Raised after <see cref="SetLanguage" /> changed the language.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>The string for a key. A missing key returns the key itself, so a gap is visible instead of blank.</summary>
    string this[string key] { get; }

    /// <summary>A translated format string applied to <paramref name="arguments" />.</summary>
    string Format(string key, params object?[] arguments);

    /// <summary>
    /// Keys looked up and not found, in order. A test asserts this is empty for every language; a running launcher only
    /// pays for the miss and keeps going.
    /// </summary>
    IReadOnlyCollection<string> MissingKeys { get; }

    /// <summary>Switches language. An unknown code falls back to the default rather than failing.</summary>
    void SetLanguage(string language);
}
