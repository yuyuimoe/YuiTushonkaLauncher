using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Tests.Domain;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("4.0.10", "4.0.10", true)]
    [InlineData("4.0.10", "4.0.11", false)]
    [InlineData(" 4.0.10 ", "4.0.10", true)]
    [InlineData("^4.0.0", "4.1.6", true)]
    [InlineData("^4.0.0", "5.0.0", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("~4.1.0", "4.1.9", true)]
    [InlineData("~4.1.0", "4.2.0", false)]
    [InlineData(">=4.0.0", "4.1.6", true)]
    [InlineData(">=4.0.0", "3.11.4", false)]
    [InlineData(">4.0.0", "4.0.0", false)]
    [InlineData("<4.0.0", "3.11.4", true)]
    [InlineData(">=3.0.0 <4.0.0", "3.11.4", true)]
    [InlineData(">=3.0.0 <4.0.0", "4.0.0", false)]
    [InlineData("=4.1.6", "4.1.6", true)]
    [InlineData(">=3.0.0, <4.0.0", "3.11.4", true)]
    [InlineData("[4.0.0,5.0.0)", "4.1.6", true)]
    [InlineData("[4.0.0,5.0.0)", "5.0.0", false)]
    [InlineData("(4.0.0,5.0.0]", "4.0.0", false)]
    [InlineData("(4.0.0,5.0.0]", "5.0.0", true)]
    [InlineData("[4.1.6]", "4.1.6", true)]
    [InlineData("[4.1.6]", "4.1.7", false)]
    [InlineData("<4.0.0 || >=4.1.0", "4.1.6", true)]
    [InlineData("<4.0.0 || >=4.1.0", "4.0.5", false)]
    [InlineData("", "4.1.6", true)]
    [InlineData("*", "4.1.6", true)]
    public void Satisfies_HandlesTheConstraintsTheEcosystemPublishes(string constraint, string version, bool expected)
    {
        VersionRange.Parse(constraint).Satisfies(SemanticVersion.Parse(version)).Should().Be(expected);
    }

    [Fact]
    public void Parse_KeepsTheRawText()
    {
        VersionRange.Parse("  ^4.0.0 ").Raw.Should().Be("^4.0.0");
    }

    [Fact]
    public void EmptyConstraint_IsAny()
    {
        VersionRange.Parse(null).Should().Be(VersionRange.Any);
        VersionRange.Parse("   ").Should().Be(VersionRange.Any);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("^")]
    [InlineData("[4.0.0")]
    public void TryParse_ReturnsFalseForGarbage(string constraint)
    {
        VersionRange.TryParse(constraint, out _).Should().BeFalse();
    }

    [Fact]
    public void Equality_IsBasedOnTheRawText()
    {
        VersionRange.Parse("^4.0.0").Should().Be(VersionRange.Parse("^4.0.0"));
        VersionRange.Parse("^4.0.0").Should().NotBe(VersionRange.Parse("^5.0.0"));
    }
}
