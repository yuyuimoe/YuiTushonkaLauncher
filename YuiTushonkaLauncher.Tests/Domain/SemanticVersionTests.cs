using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Tests.Domain;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("4.1.6", 4, 1, 6, null)]
    [InlineData(" 4.0.10 ", 4, 0, 10, null)]
    [InlineData("1.0.0-rc.1", 1, 0, 0, "rc.1")]
    [InlineData("2.5", 2, 5, 0, null)]
    [InlineData("7", 7, 0, 0, null)]
    public void Parse_AcceptsTheRealWorldShapes(string input, int major, int minor, int patch, string? preRelease)
    {
        var version = SemanticVersion.Parse(input);

        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
        version.PreRelease.Should().Be(preRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.0.0-")]
    public void TryParse_RejectsGarbage(string input)
    {
        SemanticVersion.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Compare_OrdersByNumbersThenPreRelease()
    {
        var ordered = new[] { "4.1.0-rc.1", "4.1.0", "4.1.1", "4.2.0", "5.0.0" }
            .Select(SemanticVersion.Parse)
            .Order()
            .Select(version => version.ToString());

        ordered.Should().Equal("4.1.0-rc.1", "4.1.0", "4.1.1", "4.2.0", "5.0.0");
    }

    [Fact]
    public void ComparisonOperators_WorkOnNumbersNotStrings()
    {
        var older = SemanticVersion.Parse("4.1.9");
        var newer = SemanticVersion.Parse("4.1.10");

        (newer > older).Should().BeTrue();
        (older < newer).Should().BeTrue();
        (newer >= SemanticVersion.Parse("4.1.10")).Should().BeTrue();
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        SemanticVersion.Parse("4.1.0-pre.3").ToString().Should().Be("4.1.0-pre.3");
    }

    [Fact]
    public void IsPreReleaseOf_ComparesOnlyNumbers()
    {
        SemanticVersion.Parse("4.1.0-rc.1").IsPreReleaseOf(SemanticVersion.Parse("4.1.0")).Should().BeTrue();
        SemanticVersion.Parse("4.1.0-rc.1").IsPreReleaseOf(SemanticVersion.Parse("4.1.1")).Should().BeFalse();
    }
}
