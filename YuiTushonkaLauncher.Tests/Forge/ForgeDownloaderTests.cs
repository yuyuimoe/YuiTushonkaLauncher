using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeDownloaderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly ForgeDownloader _downloader;
    private readonly string _directory;

    public ForgeDownloaderTests()
    {
        var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        _downloader = new ForgeDownloader(httpClient, NullLogger<ForgeDownloader>.Instance);
        _directory = Path.Join(Path.GetTempPath(), "ytt-download-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task DownloadAsync_WritesTheFileAndComputesItsHash()
    {
        _server
            .Given(Request.Create().WithPath("/files/mod.zip").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("mod-archive-bytes"));

        var result = await _downloader.DownloadAsync(new Uri($"{_server.Url}/files/mod.zip"), _directory, "mod.zip");

        result.FilePath.Should().Be(Path.Join(_directory, "mod.zip"));
        result.Length.Should().Be(17);
        result.Sha256.Should().Be(Sha256Of("mod-archive-bytes"));
        result.Resumed.Should().BeFalse();
        File.Exists(result.FilePath + ".part").Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_FollowsTheRedirectTheApiReturns()
    {
        _server
            .Given(Request.Create().WithPath("/mod/download/827/waypoints/1.9.0").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(307).WithHeader("Location", $"{_server.Url}/files/real.zip"));
        _server
            .Given(Request.Create().WithPath("/files/real.zip").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("redirected-bytes"));

        var result = await _downloader.DownloadAsync(new Uri($"{_server.Url}/mod/download/827/waypoints/1.9.0"), _directory, "real.zip");

        File.ReadAllText(result.FilePath).Should().Be("redirected-bytes");
    }

    [Fact]
    public async Task DownloadAsync_ResumesFromThePartialFileAndHashesTheWholeContent()
    {
        var partPath = Path.Join(_directory, "mod.zip.part");
        await File.WriteAllTextAsync(partPath, "hello");

        _server
            .Given(Request.Create().WithPath("/files/mod.zip").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(206).WithHeader("Content-Range", "bytes 5-9/10").WithBody("world"));

        var result = await _downloader.DownloadAsync(new Uri($"{_server.Url}/files/mod.zip"), _directory, "mod.zip");

        result.Resumed.Should().BeTrue();
        File.ReadAllText(result.FilePath).Should().Be("helloworld");
        result.Length.Should().Be(10);
        result.Sha256.Should().Be(Sha256Of("helloworld"));

        var rangeHeader = _server.LogEntries.Single().RequestMessage!.Headers!["Range"].Single();
        rangeHeader.Should().Be("bytes=5-");
    }

    [Fact]
    public async Task DownloadAsync_KeepsThePartialFileWhenTheHostRejectsTheRange()
    {
        var partPath = Path.Join(_directory, "mod.zip.part");
        await File.WriteAllTextAsync(partPath, "complete-already");

        _server.Given(Request.Create().WithPath("/files/mod.zip").UsingGet()).RespondWith(Response.Create().WithStatusCode(416));

        var result = await _downloader.DownloadAsync(new Uri($"{_server.Url}/files/mod.zip"), _directory, "mod.zip");

        result.Resumed.Should().BeTrue();
        File.ReadAllText(result.FilePath).Should().Be("complete-already");
        result.Sha256.Should().Be(Sha256Of("complete-already"));
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgress()
    {
        _server
            .Given(Request.Create().WithPath("/files/mod.zip").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("0123456789"));

        var reports = new List<ForgeDownloadProgress>();
        var progress = new Progress<ForgeDownloadProgress>(report => reports.Add(report));

        await _downloader.DownloadAsync(new Uri($"{_server.Url}/files/mod.zip"), _directory, "mod.zip", progress);

        // Progress is marshalled asynchronously; give it a moment, then check nothing went backwards.
        await Task.Delay(50);
        reports.Should().NotBeEmpty();
        reports.Should().BeInAscendingOrder(report => report.BytesReceived);
    }

    [Fact]
    public async Task DownloadAsync_ThrowsWhenTheHostFails()
    {
        _server.Given(Request.Create().WithPath("/files/mod.zip").UsingGet()).RespondWith(Response.Create().WithStatusCode(503));

        var act = async () => await _downloader.DownloadAsync(new Uri($"{_server.Url}/files/mod.zip"), _directory, "mod.zip");

        await act.Should().ThrowAsync<Core.Forge.ForgeUnavailableException>();
    }

    public void Dispose()
    {
        _server.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }

    private static string Sha256Of(string content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
