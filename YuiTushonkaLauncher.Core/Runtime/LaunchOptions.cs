using System.Text;

namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>Environment variables and arguments read from a Steam-style launch-options line.</summary>
public sealed record ParsedLaunchOptions(IReadOnlyDictionary<string, string> Environment, IReadOnlyList<string> Arguments);

/// <summary>
/// Reads and checks the <c>KEY=value -flag "quoted value"</c> format players already know from Steam.
/// </summary>
/// <remarks>
/// The rules mirror the official launcher: a token starting with <c>-</c>, or one with no <c>=</c>, is an
/// argument; anything else is <c>NAME=value</c>. Quotes only group, they are not part of the value.
/// </remarks>
public static class LaunchOptions
{
    /// <summary>Variables the launcher sets itself; a profile cannot override them, or two settings would fight.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "WINEPREFIX",
        "PROTONPATH",
        "GAMEID",
        "STORE",
        "STEAM_COMPAT_DATA_PATH",
        "STEAM_COMPAT_CLIENT_INSTALL_PATH",
    };

    public const int MaxVariables = 64;

    public const int MaxNameLength = 128;

    public const int MaxValueLength = 4096;

    /// <summary>The DLL override Unity Doorstop needs under Wine so BepInEx loads.</summary>
    public const string DoorstopOverride = "winhttp=n,b";

    /// <summary>Splits a line into tokens; an unterminated quote runs to the end instead of failing.</summary>
    public static IReadOnlyList<string> Tokenize(string? line)
    {
        var tokens = new List<string>();

        if (string.IsNullOrWhiteSpace(line))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var quote = '\0';
        var started = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '\\' && index + 1 < line.Length && line[index + 1] is '"' or '\'')
            {
                current.Append(line[++index]);
                started = true;
                continue;
            }

            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                started = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (started)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(character);
            started = true;
        }

        if (started)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>Separates the variables from the arguments of a launch-options line.</summary>
    public static ParsedLaunchOptions Parse(string? line)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        var arguments = new List<string>();

        foreach (var token in Tokenize(line))
        {
            var separator = token.IndexOf('=', StringComparison.Ordinal);

            if (token.StartsWith('-') || separator <= 0)
            {
                arguments.Add(token);
                continue;
            }

            // A repeated name overwrites instead of throwing, as Steam does.
            environment[token[..separator]] = token[(separator + 1)..];
        }

        return new ParsedLaunchOptions(environment, arguments);
    }

    /// <summary>The inverse of <see cref="Parse" />, so the editor can show a profile as one line.</summary>
    public static string Format(IReadOnlyDictionary<string, string> environment, IEnumerable<string> arguments)
    {
        var parts = environment.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={Quote(pair.Value)}");

        return string.Join(' ', parts.Concat(arguments.Select(Quote)));
    }

    /// <summary>
    /// Checks a set of variables and returns one message per problem (empty when it is fine). Names must be
    /// portable identifiers, values must not carry line breaks or NULs, and the launcher's own variables
    /// are off limits.
    /// </summary>
    public static IReadOnlyList<string> ValidateEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        var problems = new List<string>();

        if (environment.Count > MaxVariables)
        {
            problems.Add($"At most {MaxVariables} environment variables are allowed.");
        }

        foreach (var (name, value) in environment)
        {
            if (!IsValidName(name))
            {
                problems.Add($"'{Truncate(name)}' is not a valid variable name (letters, digits and '_', not starting with a digit).");
                continue;
            }

            if (Reserved.Contains(name))
            {
                problems.Add($"{name} is set by the launcher and cannot be overridden here.");
            }

            if (value.Length > MaxValueLength)
            {
                problems.Add($"The value of {name} is longer than {MaxValueLength} characters.");
            }

            if (value.Any(character => character is '\0' or '\r' or '\n'))
            {
                problems.Add($"The value of {name} contains a line break or a NUL character.");
            }
        }

        return problems;
    }

    /// <summary>Checks that extra arguments contain no NUL or line break, which no process accepts.</summary>
    public static IReadOnlyList<string> ValidateArguments(IEnumerable<string> arguments)
    {
        return arguments
            .Where(argument => argument.Any(character => character is '\0' or '\r' or '\n'))
            .Select(argument => $"The argument '{Truncate(argument)}' contains a line break or a NUL character.")
            .ToList();
    }

    /// <summary>Whether a <c>WINEDLLOVERRIDES</c> value makes <c>winhttp</c> native-first.</summary>
    public static bool OverridesWinhttp(string? overrides)
    {
        if (string.IsNullOrWhiteSpace(overrides))
        {
            return false;
        }

        // Overrides look like "dll1,dll2=n,b;dll3=d"; the DLL list is before '=', the mode after.
        foreach (var group in overrides.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = group.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            var dlls = group[..separator].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var mode = group[(separator + 1)..].Trim();

            if (
                dlls.Contains("winhttp", StringComparer.OrdinalIgnoreCase)
                && (mode == "n" || mode.StartsWith("n,", StringComparison.Ordinal))
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidName(string name)
    {
        return name.Length is > 0 and <= MaxNameLength
            && (char.IsAsciiLetter(name[0]) || name[0] == '_')
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static string Quote(string value)
    {
        return value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character is not ('"' or '\''))
            ? value
            : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Truncate(string value)
    {
        return value.Length <= 40 ? value : value[..40] + "…";
    }
}
