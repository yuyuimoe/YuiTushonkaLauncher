using System;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// Sorting and version filtering are the part the API dictates: the sort names below are the only ones it accepts
/// (verified against the live API), and a date window is the only way to approximate a ranking over a period.
/// </summary>
public sealed class ForgeBrowseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Featured_AsksTheFlagAndCarriesNoWindow()
    {
        var query = ForgeSorts.Apply(new ForgeModQuery(), ForgeSort.Featured, Now);

        query.Sort.Should().Be("featured");
        query.PublishedFrom.Should().BeNull();
        query.PublishedTo.Should().BeNull();
    }

    [Fact]
    public void EndorsedWeek_IsEndorsementsInsideASevenDayWindow()
    {
        var query = ForgeSorts.Apply(new ForgeModQuery(), ForgeSort.EndorsedWeek, Now);

        query.Sort.Should().Be("-endorsements_count");
        query.PublishedFrom.Should().Be(Now.AddDays(-7));
        query.PublishedTo.Should().Be(Now);
    }

    [Fact]
    public void EndorsedMonth_IsEndorsementsInsideAThirtyDayWindow()
    {
        var query = ForgeSorts.Apply(new ForgeModQuery(), ForgeSort.EndorsedMonth, Now);

        query.Sort.Should().Be("-endorsements_count");
        query.PublishedFrom.Should().Be(Now.AddDays(-30));
    }

    [Fact]
    public void EndorsedAllTime_HasNoWindow()
    {
        var query = ForgeSorts.Apply(new ForgeModQuery(), ForgeSort.EndorsedAllTime, Now);

        query.Sort.Should().Be("-endorsements_count");
        query.PublishedFrom.Should().BeNull();
        query.PublishedTo.Should().BeNull();
    }

    [Theory]
    [InlineData(ForgeSort.Newest, "-published_at")]
    [InlineData(ForgeSort.RecentlyUpdated, "-updated_at")]
    [InlineData(ForgeSort.Name, "name")]
    public void TheRemainingOrders(ForgeSort sort, string expected)
    {
        ForgeSorts.Apply(new ForgeModQuery(), sort, Now).Sort.Should().Be(expected);
    }

    [Fact]
    public void SortingKeepsTheFiltersTheQueryAlreadyHad()
    {
        var query = ForgeSorts.Apply(
            new ForgeModQuery
            {
                Search = "sain",
                CategoryId = 5,
                SptVersion = new SemanticVersion(4, 1, 6),
            },
            ForgeSort.Newest,
            Now
        );

        query.Search.Should().Be("sain");
        query.CategoryId.Should().Be(5);
        query.SptVersion!.Value.ToString().Should().Be("4.1.6");
    }

    [Fact]
    public void EveryOrderingHasALabelKey()
    {
        ForgeSorts.All.Should().HaveCount(7);
        ForgeSorts.All.Should().OnlyHaveUniqueItems(choice => choice.Sort);
        ForgeSorts.All.Should().OnlyContain(choice => choice.LabelKey.StartsWith("forge.sort", StringComparison.Ordinal));
    }

    private static ForgeModVersion Version(string version, params string[] supported)
    {
        return new ForgeModVersion
        {
            Id = 1,
            Version = SemanticVersion.Parse(version),
            SptConstraint = VersionRange.Parse("*"),
            DownloadUrl = "https://example.invalid/mod.zip",
            SupportedSptVersions = [.. supported.Select(SemanticVersion.Parse)],
        };
    }

    [Fact]
    public void AVersionListingItsSptVersionsIsJudgedByThatList()
    {
        var version = Version("1.0.0", "4.1.6", "4.1.5");

        ForgeVersionCompatibility.IsCompatible(version, new SemanticVersion(4, 1, 6)).Should().BeTrue();
        ForgeVersionCompatibility.IsCompatible(version, new SemanticVersion(4, 0, 13)).Should().BeFalse();
    }

    [Fact]
    public void WithoutAListTheConstraintDecides()
    {
        var version = Version("1.0.0") with { SptConstraint = VersionRange.Parse("~4.1") };

        ForgeVersionCompatibility.IsCompatible(version, new SemanticVersion(4, 1, 6)).Should().BeTrue();
        ForgeVersionCompatibility.IsCompatible(version, new SemanticVersion(4, 0, 13)).Should().BeFalse();
    }

    [Fact]
    public void NoInstanceAcceptsEveryVersion()
    {
        ForgeVersionCompatibility.IsCompatible(Version("1.0.0", "3.0.0"), null).Should().BeTrue();
    }

    [Fact]
    public void Compatible_PicksTheUsableOnesNewestFirst()
    {
        var versions = new[] { Version("1.0.0", "4.1.6"), Version("1.2.0", "4.1.6"), Version("0.9.0", "4.0.0") };

        var usable = ForgeVersionCompatibility.Compatible(versions, new SemanticVersion(4, 1, 6));

        usable.Should().HaveCount(2);
        usable[0].Version.ToString().Should().Be("1.2.0");
        usable[1].Version.ToString().Should().Be("1.0.0");
    }
}
