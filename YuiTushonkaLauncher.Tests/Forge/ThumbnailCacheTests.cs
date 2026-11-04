using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// The thumbnail cache exists so a long result list never decodes an image on the UI thread and never downloads the
/// same picture twice. It answers with a path or with <c>null</c>, and it never lets the folder grow without a bound.
/// </summary>
public sealed class ThumbnailCacheTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private readonly StubHandler _handler = new();

    public void Dispose()
    {
        _temp.Dispose();
    }

    private ThumbnailCache Cache()
    {
        var paths = _temp.CreateAppPaths();
        paths.EnsureCreated();
        return new ThumbnailCache(paths, NullLogger<ThumbnailCache>.Instance, new HttpClient(_handler));
    }

    [Fact]
    public async Task Get_DownloadsOnceAndServesTheSameFileAfterwards()
    {
        using var cache = Cache();

        var first = await cache.GetAsync("https://files.test/mod.jpg");
        var second = await cache.GetAsync("https://files.test/mod.jpg");

        first.Should().NotBeNull();
        File.Exists(first!).Should().BeTrue();
        second.Should().Be(first);
        _handler.Calls.Should().Be(1, "the second request is served from the disk");
    }

    [Fact]
    public async Task Get_ReturnsNullForAnEmptyOrNonHttpUrl()
    {
        using var cache = Cache();

        (await cache.GetAsync(null)).Should().BeNull();
        (await cache.GetAsync(string.Empty)).Should().BeNull();
        (await cache.GetAsync("/relative/path.jpg")).Should().BeNull();
        (await cache.GetAsync("file:///etc/passwd")).Should().BeNull();
        _handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Get_ReturnsNullWhenTheImageIsMissing()
    {
        _handler.Status = HttpStatusCode.NotFound;
        using var cache = Cache();

        (await cache.GetAsync("https://files.test/missing.jpg")).Should().BeNull();
    }

    [Fact]
    public async Task Get_RefusesAnImageLargerThanTheCap()
    {
        _handler.Body = new byte[600 * 1024];
        using var cache = Cache();

        (await cache.GetAsync("https://files.test/huge.jpg")).Should().BeNull();
        _handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Get_SendsTheRefererTheFileHostRequires()
    {
        // Measured: https://files.sp-mod.com answers 403 with no Referer and 200 with the site as one.
        using var cache = Cache();

        await cache.GetAsync("https://files.test/mod.png");

        _handler.Referers.Should().ContainSingle().Which.Should().Be("https://sp-mod.com/");
    }

    [Fact]
    public async Task Get_KeepsTheFolderInsideItsCount()
    {
        using var cache = Cache();

        for (var index = 0; index < 210; index++)
        {
            await cache.GetAsync($"https://files.test/{index}.jpg");
        }

        var folder = Path.Combine(_temp.CreateAppPaths().CacheDirectory, "thumbnails");
        Directory.GetFiles(folder).Length.Should().BeLessThanOrEqualTo(200);
    }

    /// <summary>Answers with a JPEG, counts the calls, and records nothing else.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Referers { get; } = [];

        public int Count => Referers.Count;

        public int Calls { get; private set; }

        public byte[] Body { get; set; } = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Headers.Referrer is { } referrer)
            {
                Referers.Add(referrer.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) });
        }
    }
}
