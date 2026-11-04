using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>
/// Turns the HTML a mod's description arrives in into readable lines. The launcher has no web view and no intention of
/// gaining one for a text field: images and scripts are dropped, paragraphs become blank lines, and the result is what
/// a text block can render.
/// </summary>
public static partial class ForgeDescription
{
    /// <summary>The description as plain text lines, or an empty string when there is nothing to show.</summary>
    public static string ToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = html;

        // Blocks that carry no meaning in text, including their contents.
        text = Dropped().Replace(text, " ");
        text = LineBreak().Replace(text, "\n");
        text = ParagraphEnd().Replace(text, "\n\n");
        text = ListItem().Replace(text, "\n• ");
        text = Tag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var builder = new StringBuilder(text.Length);
        var blank = 1;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                blank++;
                continue;
            }

            // At most one empty line between paragraphs: the source is full of nested tags.
            if (blank > 0 && builder.Length > 0)
            {
                builder.Append('\n');
            }

            blank = 0;
            builder.Append(trimmed).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    [GeneratedRegex(@"<(script|style|noscript|iframe|svg|video|audio)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Dropped();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"</(p|div|h[1-6]|tr|table|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEnd();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tag();
}
