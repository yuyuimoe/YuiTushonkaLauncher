using System.Linq;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Forge.Markdown;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// Descriptions arrive as Markdown and as HTML — often both in one field — so the parser is fed the shapes mod authors
/// actually publish. It must never throw: a broken description is a shorter page, not a broken window.
/// </summary>
public sealed class MarkdownParserTests
{
    [Fact]
    public void HtmlParagraphsBecomeParagraphBlocksWithTheirEmphasis()
    {
        var document = MarkdownParser.Parse("<p>First <strong>paragraph</strong>.</p><p>Second <em>one</em>.</p>");

        document.Blocks.Should().HaveCount(2);
        document.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Paragraph);
        document.Blocks[0].Inlines.Should().Contain(inline => inline.Kind == MarkdownInlineKind.Bold && inline.Text == "paragraph");
        document.Blocks[1].Inlines.Should().Contain(inline => inline.Kind == MarkdownInlineKind.Italic && inline.Text == "one");
        MarkdownParser.ToText(document).Should().Be("First paragraph.\nSecond one.");
    }

    [Theory]
    [InlineData("# About This Mod", 1)]
    [InlineData("### Changes Include:", 3)]
    [InlineData("<h2>Installation</h2>", 2)]
    public void HeadingsCarryTheirLevel(string source, int level)
    {
        var document = MarkdownParser.Parse(source);

        document.Blocks.Should().ContainSingle().Which.Kind.Should().Be(MarkdownBlockKind.Heading);
        document.Blocks[0].Level.Should().Be(level);
    }

    [Fact]
    public void BothListKindsKeepTheirItems()
    {
        var html = MarkdownParser.Parse("<ul><li>One</li><li>Two <strong>bold</strong></li></ul>");
        html.Blocks.Should().ContainSingle().Which.Kind.Should().Be(MarkdownBlockKind.Bullets);
        html.Blocks[0].Items.Should().HaveCount(2);
        html.Blocks[0].Items[1].Should().Contain(inline => inline.Kind == MarkdownInlineKind.Bold);

        var ordered = MarkdownParser.Parse("<ol><li>First</li><li>Second</li></ol>");
        ordered.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Numbers);
        MarkdownParser.ToText(ordered).Should().Be("• First\n• Second");

        var dashes = MarkdownParser.Parse("- alpha\n- beta\n1. third");
        dashes.Blocks.Should().HaveCount(2);
        dashes.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Bullets);
        dashes.Blocks[1].Kind.Should().Be(MarkdownBlockKind.Numbers);
    }

    [Fact]
    public void WrappedListItemsStayOneItemAndParagraphsSwallowTheirLines()
    {
        var document = MarkdownParser.Parse("- a long item that\n  wraps onto a second line\n- another");

        document.Blocks[0].Items.Should().HaveCount(2);
        MarkdownParser.ToText(document).Should().Be("• a long item that wraps onto a second line\n• another");

        // A single newline inside prose is a line break in the source and a space in the output.
        MarkdownParser.ToText(MarkdownParser.Parse("one line\nsecond line")).Should().Be("one line second line");
    }

    [Fact]
    public void CodeKeepsItsWhitespaceAndQuotesStayQuotes()
    {
        var document = MarkdownParser.Parse("```json\n{\n  \"a\": 1\n}\n```\n\n> quoted text");

        document.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Code);
        document.Blocks[0].Lines.Should().Equal("{", "  \"a\": 1", "}");
        document.Blocks[0].Url.Should().Be("json", "the fence's language is kept");
        document.Blocks[1].Kind.Should().Be(MarkdownBlockKind.Quote);
        document.Blocks[1].Inlines.Should().ContainSingle().Which.Text.Should().Be("quoted text");
    }

    [Fact]
    public void LinksFromBothSyntaxesKeepTheirTarget()
    {
        var markdown = MarkdownParser.Parse("See [the wiki](https://example.test/wiki) for more.");
        markdown
            .Blocks[0]
            .Inlines.Should()
            .Contain(inline =>
                inline.Kind == MarkdownInlineKind.Link && inline.Text == "the wiki" && inline.Url == "https://example.test/wiki"
            );

        var html = MarkdownParser.Parse("<p>See <a href=\"https://example.test/x\" title=\"t\">this</a>.</p>");
        html.Blocks[0]
            .Inlines.Should()
            .Contain(inline => inline.Kind == MarkdownInlineKind.Link && inline.Text == "this" && inline.Url == "https://example.test/x");

        // An incomplete link is text, not a crash.
        MarkdownParser.Parse("[not a link](").Blocks[0].Inlines.Should().OnlyContain(inline => inline.Kind != MarkdownInlineKind.Link);
    }

    [Fact]
    public void ImagesAndRulesAreTheirOwnBlocks()
    {
        var document = MarkdownParser.Parse("![preview](https://files.sp-mod.com/a.png)\n\n---\n\ntext");

        document.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Image);
        document.Blocks[0].Url.Should().Be("https://files.sp-mod.com/a.png");
        document.Blocks[0].Inlines.Should().ContainSingle().Which.Text.Should().Be("preview");
        document.Blocks[1].Kind.Should().Be(MarkdownBlockKind.Rule);
        document.Blocks[2].Kind.Should().Be(MarkdownBlockKind.Paragraph);
    }

    [Fact]
    public void InlineCodeAndStrikethroughAreRecognised()
    {
        var inlines = MarkdownParser.Parse("set `sain.cfg` and ~~remove~~ it").Blocks[0].Inlines;

        inlines.Should().Contain(inline => inline.Kind == MarkdownInlineKind.Code && inline.Text == "sain.cfg");
        inlines.Should().Contain(inline => inline.Kind == MarkdownInlineKind.Strike && inline.Text == "remove");
    }

    [Fact]
    public void ScriptsStylesAndUnclosedTagsAreHandledWithoutThrowing()
    {
        var html =
            "<style>body{color:red}</style><script>alert(1)</script><p>kept &amp; decoded<br/>line</p><div><b>unclosed <a href=\">\">";

        var document = MarkdownParser.Parse(html);

        var text = MarkdownParser.ToText(document);
        text.Should().Contain("kept & decoded");
        text.Should().NotContain("alert").And.NotContain("color:red");

        var action = () => MarkdownParser.Parse(new string('<', 5000) + new string('>', 5000));
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void NothingToShowIsAnEmptyDocument(string? source)
    {
        MarkdownParser.Parse(source).IsEmpty.Should().BeTrue();
        MarkdownParser.Parse(source).Should().BeSameAs(MarkdownDocument.Empty);
    }

    [Fact]
    public void OnlyScrollOrBoldMarkersStaysText()
    {
        MarkdownParser.ToText(MarkdownParser.Parse("2 * 3 * 4 = 24")).Should().Be("2 * 3 * 4 = 24");
    }

    [Fact]
    public void ARealDescriptionParsesIntoTheSectionsItsAuthorWrote()
    {
        const string source = """
            # About This Mod

            This is a mod that buffs the current in-game **medical items** to be more useful.

            ## Changes Include:

            * Reduced usage time on all medical items.
            * Sanitar's items have been debuffed.
            * Med Kits are configurable.

            ## Installation

            1. Drop the `BepInEx/plugins` folder into your game.
            2. Start the server.

            See the [source](https://github.com/example/medical) for details.
            """;

        var document = MarkdownParser.Parse(source);

        document
            .Blocks.Select(block => block.Kind)
            .Should()
            .ContainInOrder(
                MarkdownBlockKind.Heading,
                MarkdownBlockKind.Paragraph,
                MarkdownBlockKind.Heading,
                MarkdownBlockKind.Bullets,
                MarkdownBlockKind.Heading,
                MarkdownBlockKind.Numbers,
                MarkdownBlockKind.Paragraph
            );
        document.Blocks.Select(block => block.Level).Where(level => level > 0).Should().Equal(1, 2, 2);
        document.Blocks[3].Items.Should().HaveCount(3);
        document.Blocks[6].Inlines.Should().Contain(inline => inline.Kind == MarkdownInlineKind.Link);
    }
}
