using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>The description arrives as HTML and is shown in a text block, so the conversion is part of the contract.</summary>
public sealed class ForgeDescriptionTests
{
    [Fact]
    public void ToText_KeepsParagraphsAndDropsMarkup()
    {
        var text = ForgeDescription.ToText("<p>First <strong>paragraph</strong>.</p><p>Second one.</p>");

        text.Should().Be("First paragraph.\n\nSecond one.");
    }

    [Fact]
    public void ToText_TurnsListsIntoBullets()
    {
        var text = ForgeDescription.ToText("<ul><li>One</li><li>Two</li></ul>");

        text.Should().Be("• One\n• Two");
    }

    [Fact]
    public void ToText_DropsScriptsStylesAndImages()
    {
        var text = ForgeDescription.ToText(
            "<p>Read me</p><script>alert('x')</script><style>p{}</style><img src=\"a.png\" alt=\"a picture\" />"
        );

        text.Should().Be("Read me");
    }

    [Fact]
    public void ToText_DecodesEntitiesAndKeepsLineBreaks()
    {
        ForgeDescription.ToText("<p>a &amp; b<br/>c</p>").Should().Be("a & b\nc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToText_ReturnsEmptyForNoDescription(string? html)
    {
        ForgeDescription.ToText(html).Should().BeEmpty();
    }

    [Fact]
    public void ToText_DoesNotThrowOnBrokenMarkup()
    {
        var action = () => ForgeDescription.ToText("<p>unclosed <b>bold <a href=\">\"><p><<>>");

        action.Should().NotThrow();
    }
}
