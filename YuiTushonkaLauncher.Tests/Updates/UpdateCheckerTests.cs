using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Infrastructure.Updates;

namespace YuiTushonkaLauncher.Tests.Updates;

/// <summary>
/// The update check against a local server: a release newer than the build is offered, a development build is never told
/// to downgrade, and every failure becomes a sentence instead of an exception.
/// </summary>
public sealed class UpdateCheckerTests : IDisposable
{
    private const string LatestPath = "/repos/" + GitHubReleaseChecker.DefaultRepository + "/releases/latest";

    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose()
    {
        _server.Dispose();
    }

    private GitHubReleaseChecker Checker(string currentVersion)
    {
        return new GitHubReleaseChecker(
            new HttpClient(),
            NullLogger<GitHubReleaseChecker>.Instance,
            currentVersion,
            new Uri(_server.Url + "/")
        );
    }

    private void Respond(HttpStatusCode status, string? json = null)
    {
        var response = Response.Create().WithStatusCode(status);
        if (json is not null)
        {
            response = response.WithHeader("Content-Type", "application/json").WithBody(json);
        }

        _server.Given(Request.Create().WithPath(LatestPath).UsingGet()).RespondWith(response);
    }

    private static string Release(string tag)
    {
        return JsonSerializer.Serialize(
            new
            {
                tag_name = tag,
                html_url = "https://github.com/SP-Tushonka/YuiTushonkaLauncher/releases/tag/" + tag,
                body = "notes",
                published_at = "2026-11-01T10:00:00Z",
            }
        );
    }

    [Fact]
    public async Task ANewerReleaseIsOffered()
    {
        Respond(HttpStatusCode.OK, Release("v0.2.0"));

        var result = await Checker("0.1.0").CheckAsync();

        result.Failed.Should().BeFalse();
        result.HasUpdate.Should().BeTrue();
        result.Latest!.Version.Should().Be(SemanticVersion.Parse("0.2.0"));
        result.Latest.Tag.Should().Be("v0.2.0");
        result.Latest.Url.Should().Contain("releases/tag");
        result.Latest.PublishedAt.Should().Be(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("v0.1.0")]
    public async Task TheSameReleaseIsNotAnUpdate(string tag)
    {
        Respond(HttpStatusCode.OK, Release(tag));

        var result = await Checker("0.1.0").CheckAsync();

        result.HasUpdate.Should().BeFalse();
        result.Latest.Should().NotBeNull();
    }

    [Fact]
    public async Task AnOlderReleaseIsNotAnUpdate()
    {
        Respond(HttpStatusCode.OK, Release("v0.0.9"));

        (await Checker("0.1.0").CheckAsync()).HasUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task ADevelopmentBuildIsNeverToldToUpdate()
    {
        // A build from a sha is ahead of the last release by construction; offering the release would be a downgrade.
        Respond(HttpStatusCode.OK, Release("v0.2.0"));

        var result = await Checker("0.1.0-pre-abc1234.dirty").CheckAsync();

        result.HasUpdate.Should().BeFalse();
        result.Latest!.Version.Should().Be(SemanticVersion.Parse("0.2.0"));
    }

    [Fact]
    public async Task NoReleaseYetIsSaidInWords()
    {
        Respond(HttpStatusCode.NotFound);

        var result = await Checker("0.1.0").CheckAsync();

        result.Failed.Should().BeTrue();
        result.Reason.Should().Be("No release has been published yet.");
    }

    [Fact]
    public async Task AServerErrorIsReportedWithItsCode()
    {
        Respond(HttpStatusCode.ServiceUnavailable);

        var result = await Checker("0.1.0").CheckAsync();

        result.Failed.Should().BeTrue();
        result.Reason.Should().Contain("503");
    }

    [Fact]
    public async Task AnUnreachableServerIsNotAnException()
    {
        _server.Stop();

        var result = await Checker("0.1.0").CheckAsync();

        result.Failed.Should().BeTrue();
        result.Reason.Should().Contain("could not be read");
    }

    [Fact]
    public async Task AMalformedAnswerIsNotAnException()
    {
        Respond(HttpStatusCode.OK, "{ not json");

        (await Checker("0.1.0").CheckAsync()).Failed.Should().BeTrue();
    }

    [Fact]
    public async Task ATagThatIsNotAVersionIsRefused()
    {
        Respond(HttpStatusCode.OK, Release("latest"));

        var result = await Checker("0.1.0").CheckAsync();

        result.Failed.Should().BeTrue();
        result.Reason.Should().Contain("latest");
    }

    [Fact]
    public async Task ABuildWithoutAVersionDoesNotTry()
    {
        Respond(HttpStatusCode.OK, Release("v9.9.9"));

        var result = await Checker("unknown").CheckAsync();

        result.Failed.Should().BeTrue();
        result.Reason.Should().Contain("does not carry a version");
    }

    [Theory]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("0.1.0-pre-abc", 0, 1, 0)]
    [InlineData("1.2.3+meta", 1, 2, 3)]
    public void VersionParsingToleratesWhatTagsLookLike(string text, int major, int minor, int patch)
    {
        GitHubReleaseChecker.ParseVersion(text).Should().Be(new SemanticVersion(major, minor, patch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void VersionParsingRefusesWhatIsNotAVersion(string? text)
    {
        GitHubReleaseChecker.ParseVersion(text).Should().BeNull();
    }
}
