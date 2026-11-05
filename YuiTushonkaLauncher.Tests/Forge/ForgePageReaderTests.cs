using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// Licence, source code and VirusTotal links exist only on the site's page, so the reader is tested twice: against a
/// trimmed copy of a real page, and against what a changed or broken page looks like (no links, never an exception).
/// </summary>
public sealed class ForgePageReaderTests
{
    private static ForgePageReader Reader(HttpMessageHandler? handler = null)
    {
        return new ForgePageReader(
            new HttpClient(handler ?? new StubHandler(null)),
            NullLogger<ForgePageReader>.Instance,
            "https://sp-mod.test"
        );
    }

    private static string Fixture()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Forge", "Fixtures", "mod-page.html"));
    }

    [Fact]
    public void Parse_ReadsTheLicenceWithItsUrl()
    {
        var info = Reader().Parse(Fixture());

        info.Should().NotBeNull();
        info!.License.Should().Be("MIT License");
        info.LicenseUrl.Should().Be("https://choosealicense.com/licenses/mit/");
    }

    [Fact]
    public void Parse_ReadsTheSourceCodeLink()
    {
        var info = Reader().Parse(Fixture());

        info!.SourceCodeUrls.Should().ContainSingle().Which.Should().Be("https://github.com/DrakiaXYZ/SPT-BigBrain");
    }

    [Fact]
    public void Parse_ReadsTheVirusTotalLinkWithoutItsCacheBuster()
    {
        var info = Reader().Parse(Fixture());

        info!.VirusTotalUrls.Should().ContainSingle();
        info.VirusTotalUrls[0].Should().StartWith("https://www.virustotal.com/gui/file/140ae49d");
    }

    [Fact]
    public void Parse_ReturnsEmptyInfoForAPageWithoutTheSections()
    {
        var info = Reader().Parse("<html><body><h3>Something else</h3><p>nothing</p></body></html>");

        info.Should().NotBeNull();
        info!.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body><a href=\"not a url\">x</a>")]
    [InlineData("{{{ not html at all")]
    public void Parse_NeverThrowsOnOddInput(string html)
    {
        var action = () => Reader().Parse(html);

        action.Should().NotThrow();
    }

    [Fact]
    public async Task Read_AsksTheModPageAndParsesIt()
    {
        var handler = new StubHandler(Fixture());
        var info = await Reader(handler).ReadAsync(902, "bigbrain");

        handler.LastUri!.ToString().Should().Be("https://sp-mod.test/mod/902/bigbrain");
        info!.License.Should().Be("MIT License");
    }

    [Fact]
    public async Task Read_ReturnsNullWhenThePageIsMissing()
    {
        var info = await Reader(new StubHandler(null, HttpStatusCode.NotFound)).ReadAsync(1, "gone");

        info.Should().BeNull();
    }

    /// <summary>Answers with fixed content and records the address it was asked for.</summary>
    private sealed class StubHandler(string? body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body ?? string.Empty) });
        }
    }
}
