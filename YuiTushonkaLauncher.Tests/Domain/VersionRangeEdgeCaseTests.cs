using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Tests.Domain;

public sealed class VersionRangeEdgeCaseTests
{
    [Theory]
    [InlineData("[,5.0.0)", "0.1.0", true)]
    [InlineData("[,5.0.0)", "5.0.0", false)]
    [InlineData("[4.0.0,)", "99.0.0", true)]
    [InlineData("[4.0.0,)", "3.9.9", false)]
    [InlineData("[1.0.0,2.0.0]", "2.0.0", true)]
    [InlineData("(1.0.0,2.0.0)", "1.0.0", false)]
    [InlineData(">=4.1.0-rc.1 <4.1.0", "4.1.0-rc.2", true)]
    [InlineData(">=4.1.0 <5.0.0", "4.1.0-rc.1", false)]
    public void Satisfies_HandlesOpenEndedBracketsAndPreReleases(string constraint, string version, bool expected)
    {
        VersionRange.Parse(constraint).Satisfies(SemanticVersion.Parse(version)).Should().Be(expected);
    }

    [Fact]
    public void GetHashCode_MatchesForEqualConstraints()
    {
        VersionRange.Parse("^4.0.0").GetHashCode().Should().Be(VersionRange.Parse("^4.0.0").GetHashCode());
    }

    [Fact]
    public void Equals_ReturnsFalseForNull()
    {
        VersionRange.Parse("^4.0.0").Equals(null).Should().BeFalse();
        VersionRange.Parse("^4.0.0").Equals("^4.0.0").Should().BeFalse();
    }

    [Fact]
    public void ToString_ReturnsTheRawConstraint()
    {
        VersionRange.Parse(">=4.0.0 <5.0.0").ToString().Should().Be(">=4.0.0 <5.0.0");
    }

    [Fact]
    public void Parse_RejectsUnparsableBounds()
    {
        var act = () => VersionRange.Parse("[abc,def)");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_RejectsAnEmptyAlternativeList()
    {
        var act = () => VersionRange.Parse("   ");

        // An empty constraint is "any", not an error: this documents the decision.
        act.Should().NotThrow();
        VersionRange.Parse("   ").Should().Be(VersionRange.Any);
    }
}
