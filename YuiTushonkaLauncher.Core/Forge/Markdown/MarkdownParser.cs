using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace YuiTushonkaLauncher.Core.Forge.Markdown;

/// <summary>
/// Turns a mod description into blocks the launcher can draw. Descriptions on the forge are Markdown, but the API
/// serves parts of them already converted to HTML, so the text is first brought back to one shape (Markdown) and then
/// parsed once. Nothing here is a full CommonMark implementation: it covers what mod authors actually write — headings,
/// paragraphs, both list kinds, code, quotes, rules, images, and the inline emphasis and links.
/// </summary>
public static partial class MarkdownParser
{
    /// <summary>Parses a description, in Markdown or HTML or both. Never throws.</summary>
    public static MarkdownDocument Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return MarkdownDocument.Empty;
        }

        IReadOnlyList<string> lines;
        try
        {
            lines = MarkdownLines(HtmlToMarkdown(source));
        }
        catch (RegexMatchTimeoutException)
        {
            return MarkdownDocument.Empty;
        }

        return MarkdownDocument.From([.. ParseBlocks(lines)]);
    }

    /// <summary>The plain text of a parsed description, for search and tooltips.</summary>
    public static string ToText(MarkdownDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            var text = block.Kind switch
            {
                MarkdownBlockKind.Bullets or MarkdownBlockKind.Numbers => string.Join(
                    "\n",
                    block.Items.Select(item => "• " + InlineText(item))
                ),
                MarkdownBlockKind.Code or MarkdownBlockKind.Quote => string.Join("\n", block.Lines),
                MarkdownBlockKind.Rule => string.Empty,
                MarkdownBlockKind.Image => $"[{InlineText(block.Inlines)}]({block.Url})",
                _ => InlineText(block.Inlines),
            };

            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    private static string InlineText(IReadOnlyList<MarkdownInline> inlines)
    {
        return string.Concat(inlines.Select(inline => inline.Text)).Trim();
    }

    /// <summary>Splits on line ends and drops the padding around the whole document.</summary>
    private static IReadOnlyList<string> MarkdownLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var start = 0;
        var end = lines.Length;
        while (start < end && lines[start].Trim().Length == 0)
        {
            start++;
        }

        while (end > start && lines[end - 1].Trim().Length == 0)
        {
            end--;
        }

        return lines[start..end];
    }

    private static IEnumerable<MarkdownBlock> ParseBlocks(IReadOnlyList<string> lines)
    {
        var blocks = new List<MarkdownBlock>();
        var index = 0;
        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Trim().Length == 0)
            {
                index++;
                continue;
            }

            if (Fence().Match(line) is { Success: true } fence)
            {
                index = ReadCode(lines, index, fence.Groups[1].Value, blocks);
                continue;
            }

            if (Heading().Match(line) is { Success: true } heading)
            {
                blocks.Add(
                    new MarkdownBlock(MarkdownBlockKind.Heading)
                    {
                        Level = heading.Groups[1].Value.Length,
                        Inlines = ParseInline(heading.Groups[2].Value.Trim()),
                    }
                );
                index++;
                continue;
            }

            if (Rule().IsMatch(line))
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule));
                index++;
                continue;
            }

            if (Quote().IsMatch(line))
            {
                var quoted = new List<string>();
                while (index < lines.Count && Quote().Match(lines[index]) is { Success: true } match)
                {
                    quoted.Add(match.Groups[1].Value);
                    index++;
                }

                blocks.Add(
                    new MarkdownBlock(MarkdownBlockKind.Quote)
                    {
                        Lines = [.. quoted.Select(text => text.Trim())],
                        Inlines = ParseInline(string.Join(' ', quoted.Select(text => text.Trim()))),
                    }
                );
                continue;
            }

            if (Bullet().IsMatch(line))
            {
                index = ReadList(lines, index, Bullet(), MarkdownBlockKind.Bullets, blocks);
                continue;
            }

            if (Number().IsMatch(line))
            {
                index = ReadList(lines, index, Number(), MarkdownBlockKind.Numbers, blocks);
                continue;
            }

            if (ImageLine().Match(line.Trim()) is { Success: true } image)
            {
                blocks.Add(
                    new MarkdownBlock(MarkdownBlockKind.Image)
                    {
                        Url = image.Groups[2].Value,
                        Inlines = [MarkdownInline.Plain(image.Groups[1].Value)],
                    }
                );
                index++;
                continue;
            }

            var paragraph = new List<string>();
            while (index < lines.Count && lines[index].Trim().Length > 0 && !IsBlockStart(lines[index], startsList: paragraph.Count > 0))
            {
                paragraph.Add(lines[index].Trim());
                index++;
            }

            var text = string.Join(' ', paragraph);
            if (text.Length > 0)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph) { Inlines = ParseInline(text) });
            }
        }

        return blocks;
    }

    private static bool IsBlockStart(string line, bool startsList)
    {
        if (Fence().IsMatch(line) || Heading().IsMatch(line) || Rule().IsMatch(line) || Quote().IsMatch(line))
        {
            return true;
        }

        // A paragraph swallows the lines that follow it, except a list: mod authors put "- item" right after a line
        // without a blank line between them.
        return startsList ? Bullet().IsMatch(line) || Number().IsMatch(line) : false;
    }

    private static int ReadCode(IReadOnlyList<string> lines, int index, string language, List<MarkdownBlock> blocks)
    {
        var body = new List<string>();
        index++;
        while (index < lines.Count && !Fence().Match(lines[index]).Success)
        {
            body.Add(lines[index]);
            index++;
        }

        // The closing fence is optional in a truncated description.
        if (index < lines.Count)
        {
            index++;
        }

        blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code) { Lines = body, Url = language });
        return index;
    }

    private static int ReadList(IReadOnlyList<string> lines, int index, Regex marker, MarkdownBlockKind kind, List<MarkdownBlock> blocks)
    {
        var items = new List<IReadOnlyList<MarkdownInline>>();
        while (index < lines.Count && marker.Match(lines[index]) is { Success: true } match)
        {
            var item = new List<string> { match.Groups[1].Value.Trim() };
            index++;

            // A list item wraps onto indented lines; a blank line or a new marker ends it.
            while (
                index < lines.Count
                && lines[index].Trim().Length > 0
                && !marker.IsMatch(lines[index])
                && !Bullet().IsMatch(lines[index])
                && !Number().IsMatch(lines[index])
                && !Heading().IsMatch(lines[index])
                && !Fence().IsMatch(lines[index])
                && !Quote().IsMatch(lines[index])
            )
            {
                item.Add(lines[index].Trim());
                index++;
            }

            items.Add(ParseInline(string.Join(' ', item).Trim()));
        }

        blocks.Add(new MarkdownBlock(kind) { Items = items });
        return index;
    }

    /// <summary>Splits running text into styled runs. Unknown or unbalanced markers stay as text.</summary>
    internal static IReadOnlyList<MarkdownInline> ParseInline(string text)
    {
        var inlines = new List<MarkdownInline>();
        if (text.Length == 0)
        {
            return inlines;
        }

        var plain = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            if (TryLink(text, index, out var link, out var afterLink))
            {
                Flush();
                inlines.Add(link);
                index = afterLink;
                continue;
            }

            if (TryEmphasis(text, index, out var styled, out var afterStyled))
            {
                Flush();
                inlines.Add(styled);
                index = afterStyled;
                continue;
            }

            plain.Append(text[index]);
            index++;
        }

        Flush();
        return inlines;

        void Flush()
        {
            if (plain.Length == 0)
            {
                return;
            }

            inlines.Add(MarkdownInline.Plain(plain.ToString()));
            plain.Clear();
        }
    }

    private static bool TryLink(string text, int index, out MarkdownInline inline, out int after)
    {
        inline = MarkdownInline.Plain(string.Empty);
        after = index;
        if (text[index] != '[')
        {
            return false;
        }

        var closingText = text.IndexOf(']', index + 1);
        if (closingText < 0 || closingText + 1 >= text.Length || text[closingText + 1] != '(')
        {
            return false;
        }

        var closingUrl = text.IndexOf(')', closingText + 2);
        if (closingUrl < 0)
        {
            return false;
        }

        var label = text[(index + 1)..closingText];
        var url = text[(closingText + 2)..closingUrl].Trim();
        if (label.Length == 0 || url.Length == 0)
        {
            return false;
        }

        inline = new MarkdownInline(MarkdownInlineKind.Link, label, url);
        after = closingUrl + 1;
        return true;
    }

    private static bool TryEmphasis(string text, int index, out MarkdownInline inline, out int after)
    {
        inline = MarkdownInline.Plain(string.Empty);
        after = index;
        var marker = text[index];
        if (marker is not ('*' or '_' or '`' or '~'))
        {
            return false;
        }

        // Two markers are bold, strikethrough and code; one is italic (code takes exactly one backtick pair).
        var doubled = index + 1 < text.Length && text[index + 1] == marker && marker != '`';
        var width = doubled ? 2 : 1;
        var closing = text.IndexOf(new string(marker, width), index + width, StringComparison.Ordinal);
        if (closing <= index + width)
        {
            return false;
        }

        var body = text[(index + width)..closing];
        if (body.Length == 0 || body.Trim().Length == 0)
        {
            return false;
        }

        // A marker hugging whitespace is punctuation, not emphasis: "2 * 3 * 4" is arithmetic.
        if (char.IsWhiteSpace(text[index + width]) || char.IsWhiteSpace(text[closing - 1]))
        {
            return false;
        }

        var kind = (marker, doubled) switch
        {
            ('`', _) => MarkdownInlineKind.Code,
            ('~', true) => MarkdownInlineKind.Strike,
            ('*' or '_', true) => MarkdownInlineKind.Bold,
            _ => MarkdownInlineKind.Italic,
        };

        inline = new MarkdownInline(kind, body.Trim());
        after = closing + width;
        return true;
    }

    /// <summary>Brings HTML back to Markdown, so the parser only ever sees one syntax.</summary>
    internal static string HtmlToMarkdown(string html)
    {
        var text = html;

        text = Dropped().Replace(text, " ");
        text = Comments().Replace(text, " ");
        text = Preformatted().Replace(text, static match => "\n```\n" + WebUtility.HtmlDecode(match.Groups[1].Value) + "\n```\n");
        text = Image()
            .Replace(text, static match => $"\n![{Attribute(match.Groups[1].Value, "alt")}]({Attribute(match.Groups[1].Value, "src")})\n");
        text = Anchor()
            .Replace(
                text,
                static match => $"[{WebUtility.HtmlDecode(match.Groups[2].Value).Trim()}]({Attribute(match.Groups[1].Value, "href")})"
            );
        text = Strong().Replace(text, "**$2**");
        text = Emphasis().Replace(text, "*$2*");
        text = InlineCode().Replace(text, "`$1`");
        text = HeadingTag()
            .Replace(
                text,
                static match => "\n" + new string('#', match.Groups[1].Value[0] - '0') + " " + match.Groups[2].Value.Trim() + "\n"
            );
        text = ListTag().Replace(text, static match => OrderedList(match.Groups[2].Value, ordered: match.Groups[1].Value is "ol"));
        text = LineBreak().Replace(text, "\n");
        text = RuleTag().Replace(text, "\n---\n");
        text = CellTag().Replace(text, " | ");
        text = BlockEnd().Replace(text, "\n\n");
        text = AnyTag().Replace(text, string.Empty);

        return WebUtility.HtmlDecode(text);
    }

    /// <summary>Turns the items of one list tag into marker lines, numbering them when the list is ordered.</summary>
    private static string OrderedList(string body, bool ordered)
    {
        var items = ListItemTag().Matches(body);
        if (items.Count == 0)
        {
            return "\n";
        }

        var builder = new StringBuilder("\n");
        for (var index = 0; index < items.Count; index++)
        {
            var text = WebUtility.HtmlDecode(ListItemInnerTag().Replace(items[index].Groups[1].Value, string.Empty)).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            builder.Append(ordered ? $"{index + 1}. " : "- ").Append(Collapse(text)).Append('\n');
        }

        return builder.Append('\n').ToString();
    }

    private static string Collapse(string text)
    {
        return Whitespace().Replace(text, " ").Trim();
    }

    private static string Attribute(string tag, string name)
    {
        var match = Regex.Match(
            tag,
            $@"\b{name}\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        );
        if (!match.Success)
        {
            return string.Empty;
        }

        var value =
            match.Groups[2].Success ? match.Groups[2].Value
            : match.Groups[3].Success ? match.Groups[3].Value
            : match.Groups[4].Value;
        return WebUtility.HtmlDecode(value);
    }

    [GeneratedRegex(
        @"<(script|style|noscript|iframe|svg|video|audio|form|button)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        1000
    )]
    private static partial Regex Dropped();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, 1000)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<pre\b[^>]*>(.*?)</pre\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Preformatted();

    [GeneratedRegex(@"<img\b([^>]*)>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Image();

    [GeneratedRegex(@"<a\b([^>]*)>(.*?)</a\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"<(strong|b)\b[^>]*>(.*?)</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Strong();

    [GeneratedRegex(@"<(em|i)\b[^>]*>(.*?)</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"<code\b[^>]*>(.*?)</code\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex HeadingTag();

    [GeneratedRegex(@"<(ul|ol)\b[^>]*>(.*?)</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ListTag();

    [GeneratedRegex(@"<li\b[^>]*>(.*?)(?=<li\b|</li\s*>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ListItemTag();

    [GeneratedRegex(@"<(/?)li\b[^>]*>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ListItemInnerTag();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<hr\b[^>]*>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex RuleTag();

    [GeneratedRegex(@"</?(td|th)\b[^>]*>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex CellTag();

    [GeneratedRegex(@"</?(p|div|tr|table|section|article|blockquote|ul|ol|li)\b[^>]*>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline, 1000)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+", RegexOptions.None, 1000)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s*```\s*([\w+#-]*)", RegexOptions.None, 1000)]
    private static partial Regex Fence();

    [GeneratedRegex(@"^\s*(#{1,6})\s+(.*)$", RegexOptions.None, 1000)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s{0,3}((-\s*){3,}|(\*\s*){3,}|(_\s*){3,})$", RegexOptions.None, 1000)]
    private static partial Regex Rule();

    [GeneratedRegex(@"^\s*>\s?(.*)$", RegexOptions.None, 1000)]
    private static partial Regex Quote();

    [GeneratedRegex(@"^\s*[-*+]\s+(.*)$", RegexOptions.None, 1000)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*\d+[.)]\s+(.*)$", RegexOptions.None, 1000)]
    private static partial Regex Number();

    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)]+)\)$", RegexOptions.None, 1000)]
    private static partial Regex ImageLine();
}
