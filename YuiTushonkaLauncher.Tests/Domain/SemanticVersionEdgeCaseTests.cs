using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Tests.Domain;

public sealed class SemanticVersionEdgeCaseTests
{
    [Fact]
    public void TryParse_RejectsNull()
    {
        SemanticVersion.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_ThrowsWithTheOffendingText()
    {
        var act = () => SemanticVersion.Parse("not-a-version");

        act.Should().Throw<FormatException>().WithMessage("*not-a-version*");
    }

    [Fact]
    public void Compare_OrdersTwoPreReleasesLexically()
    {
        var alpha = new SemanticVersion(1, 0, 0, "alpha");
        var beta = new SemanticVersion(1, 0, 0, "beta");

        alpha.CompareTo(beta).Should().BeNegative();
        (alpha <= beta).Should().BeTrue();
    }

    [Fact]
    public void Compare_IsZeroForIdenticalVersions()
    {
        var version = SemanticVersion.Parse("4.1.6");

        version.CompareTo(SemanticVersion.Parse("4.1.6")).Should().Be(0);
        version.IsPreReleaseOf(SemanticVersion.Parse("4.1.6")).Should().BeFalse();
    }

    [Fact]
    public void PreRelease_IgnoresWhitespace()
    {
        new SemanticVersion(1, 0, 0, "   ").PreRelease.Should().BeNull();
        new SemanticVersion(1, 0, 0, " rc.1 ").PreRelease.Should().Be(" rc.1 ");
    }

    [Fact]
    public void Default_CtorYieldsZeroVersion()
    {
        default(SemanticVersion).ToString().Should().Be("0.0.0");
    }
}
