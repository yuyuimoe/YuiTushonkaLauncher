namespace YuiTushonkaLauncher.Core.Forge.Markdown;

/// <summary>What a block of a description is.</summary>
public enum MarkdownBlockKind
{
    /// <summary>Running text.</summary>
    Paragraph,

    /// <summary>A section title, with <see cref="MarkdownBlock.Level" /> saying how deep.</summary>
    Heading,

    /// <summary>An unordered list; every item is a list of inlines.</summary>
    Bullets,

    /// <summary>An ordered list, rendered with its numbers.</summary>
    Numbers,

    /// <summary>A code block, kept line by line because its whitespace matters.</summary>
    Code,

    /// <summary>A block quote.</summary>
    Quote,

    /// <summary>A horizontal rule, which the site uses to separate sections.</summary>
    Rule,

    /// <summary>An image, kept as alt text and address: the launcher shows the address, it does not load it.</summary>
    Image,
}

/// <summary>What a piece of running text is.</summary>
public enum MarkdownInlineKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Bold.</summary>
    Bold,

    /// <summary>Italic.</summary>
    Italic,

    /// <summary>Struck through.</summary>
    Strike,

    /// <summary>Inline code.</summary>
    Code,

    /// <summary>A link; <see cref="MarkdownInline.Url" /> holds the target.</summary>
    Link,
}

/// <summary>A run of text with one style. Links carry their own target.</summary>
/// <param name="Kind">How the run is styled.</param>
/// <param name="Text">The text itself, already decoded.</param>
/// <param name="Url">Where the run points, for <see cref="MarkdownInlineKind.Link" /> runs.</param>
public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text, string? Url = null)
{
    /// <summary>Plain text, the common case.</summary>
    public static MarkdownInline Plain(string text)
    {
        return new MarkdownInline(MarkdownInlineKind.Text, text);
    }
}

/// <summary>One block of a parsed description.</summary>
/// <param name="Kind">What the block is.</param>
public sealed record MarkdownBlock(MarkdownBlockKind Kind)
{
    /// <summary>How deep a heading is: 1 for <c>#</c>, 6 for <c>######</c>.</summary>
    public int Level { get; init; }

    /// <summary>The text of a paragraph, heading, quote or image caption.</summary>
    public IReadOnlyList<MarkdownInline> Inlines { get; init; } = [];

    /// <summary>The items of a list; each item is its own run of inlines.</summary>
    public IReadOnlyList<IReadOnlyList<MarkdownInline>> Items { get; init; } = [];

    /// <summary>The lines of a code block, whitespace preserved.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>The address of an image, kept so the reader can be told where it lives.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// A mod description, parsed. The forge stores descriptions as Markdown and serves them with parts already turned into
/// HTML, so both forms arrive; <see cref="MarkdownParser" /> accepts either and produces this.
/// </summary>
public sealed class MarkdownDocument
{
    /// <summary>Nothing to show.</summary>
    public static readonly MarkdownDocument Empty = new([]);

    private MarkdownDocument(IReadOnlyList<MarkdownBlock> blocks)
    {
        Blocks = blocks;
        IsEmpty = blocks.Count == 0;
    }

    /// <summary>The blocks, in document order.</summary>
    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    /// <summary>True when there is nothing to render.</summary>
    public bool IsEmpty { get; } = false;

    internal static MarkdownDocument From(IReadOnlyList<MarkdownBlock> blocks)
    {
        return blocks.Count == 0 ? Empty : new MarkdownDocument(blocks);
    }
}
