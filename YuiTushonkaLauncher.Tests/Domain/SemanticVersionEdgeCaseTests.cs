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

    [Theory]
    [InlineData("0.3.1+1", 0, 3, 1, null, "1")]
    [InlineData("1.8.0+h4", 1, 8, 0, null, "h4")]
    [InlineData("1.0.0-rc.1+build.5", 1, 0, 0, "rc.1", "build.5")]
    [InlineData("2.0.0+20130313144700", 2, 0, 0, null, "20130313144700")]
    [InlineData("1.0.0+exp.sha.5114f85", 1, 0, 0, null, "exp.sha.5114f85")]
    [InlineData("1.0.0+with-hyphen", 1, 0, 0, null, "with-hyphen")]
    [InlineData(" 3.4.5+x ", 3, 4, 5, null, "x")]
    public void Parse_AcceptsBuildMetadata(string text, int major, int minor, int patch, string? preRelease, string build)
    {
        var version = SemanticVersion.Parse(text);

        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
        version.PreRelease.Should().Be(preRelease);
        version.Build.Should().Be(build);
    }

    [Theory]
    [InlineData("1.0.0+")]
    [InlineData("+build")]
    [InlineData("1.0.0-+x")]
    [InlineData("1.0.x+1")]
    public void TryParse_RejectsMalformedBuildMetadata(string text)
    {
        SemanticVersion.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void BuildMetadata_IsIgnoredByComparisonEqualityAndHashing()
    {
        var plain = SemanticVersion.Parse("1.8.0");
        var built = SemanticVersion.Parse("1.8.0+h4");
        var other = SemanticVersion.Parse("1.8.0+h5");

        built.Should().Be(plain);
        built.Should().Be(other);
        built.CompareTo(other).Should().Be(0);
        (built == plain).Should().BeTrue();
        (built < other).Should().BeFalse();
        built.GetHashCode().Should().Be(plain.GetHashCode());
        new HashSet<SemanticVersion> { plain, built, other }
            .Should()
            .HaveCount(1);
    }

    [Fact]
    public void BuildMetadata_DoesNotOverrideAPreReleaseDifference()
    {
        SemanticVersion.Parse("1.0.0-rc.1+a").Should().NotBe(SemanticVersion.Parse("1.0.0+a"));
        SemanticVersion.Parse("1.0.0-rc.1+a").Should().BeLessThan(SemanticVersion.Parse("1.0.0+a"));
    }

    [Theory]
    [InlineData("0.3.1+1")]
    [InlineData("1.0.0-rc.1+build.5")]
    [InlineData("1.2.3")]
    [InlineData("4.1.6-RELEASE")]
    public void ToString_PreservesBuildMetadata_SoItSurvivesTheDatabaseAndAPackage(string text)
    {
        SemanticVersion.Parse(text).ToString().Should().Be(text);
    }

    [Fact]
    public void Constructor_TreatsABlankBuildAsNone()
    {
        new SemanticVersion(1, 2, 3, null, "  ").Build.Should().BeNull();
    }
}
