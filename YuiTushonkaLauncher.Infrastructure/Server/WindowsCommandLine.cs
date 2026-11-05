using System.Text;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Builds the single command line <c>CreateProcessW</c> wants out of the argument list the launcher has, using the quoting
/// rules <c>CommandLineToArgvW</c> reads back. Pure string work, so it is tested everywhere and not only where a Windows
/// machine runs the tests.
/// </summary>
internal static class WindowsCommandLine
{
    /// <summary>
    /// The Microsoft C runtime's quoting rules, which are what every program started from a command line expects: quotes
    /// only when the argument needs them, and every backslash that precedes a quote doubled.
    /// </summary>
    public static string Build(LaunchCommand command)
    {
        var builder = new StringBuilder(Quote(command.FileName));

        foreach (var argument in command.Arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    /// <summary>One argument, quoted the way <c>CommandLineToArgvW</c> reads it back.</summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder("\"");

        for (var index = 0; index < argument.Length; index++)
        {
            var backslashes = 0;

            while (index < argument.Length && argument[index] == '\\')
            {
                backslashes++;
                index++;
            }

            if (index == argument.Length)
            {
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[index] == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(argument[index]);
            }
        }

        return builder.Append('"').ToString();
    }
}
