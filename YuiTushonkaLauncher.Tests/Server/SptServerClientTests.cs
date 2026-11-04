using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class SptServerClientTests : IDisposable
{
    private readonly StubHandler _handler = new();

    private readonly SptServerClient _client;

    public SptServerClientTests()
    {
        _client = new SptServerClient(new ServerEndpoint("0.0.0.0", 6970), _handler);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    public static byte[] Zlib(string json)
    {
        using var output = new MemoryStream();

        using (var deflater = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflater.Write(Encoding.UTF8.GetBytes(json));
        }

        return output.ToArray();
    }

    private static string Unzlib(byte[] bytes)
    {
        using var inflater = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var reader = new StreamReader(inflater);
        return reader.ReadToEnd();
    }

    [Fact]
    public void BaseUri_IsTheConnectableAddressOfTheEndpoint()
    {
        _client.BaseUri.Should().Be(new Uri("https://127.0.0.1:6970"));
    }

    [Fact]
    public async Task Ping_IsTrue_OnlyForPong()
    {
        _handler.Reply("/launcher/v2/ping", """{"Response":"Pong!"}""");
        (await _client.PingAsync()).Should().BeTrue();

        _handler.Reply("/launcher/v2/ping", """{"Response":"Nope"}""");
        (await _client.PingAsync()).Should().BeFalse();

        _handler.Reply("/launcher/v2/ping", """{"Response":true}""");
        (await _client.PingAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Ping_IsFalse_ForAnythingThatIsNotTheLauncherApi()
    {
        _handler.ReplyRaw("/launcher/v2/ping", Encoding.UTF8.GetBytes("<html>hello</html>"));
        (await _client.PingAsync()).Should().BeFalse("an HTML page on that port is not an SPT server");

        _handler.ReplyStatus("/launcher/v2/ping", HttpStatusCode.InternalServerError);
        (await _client.PingAsync()).Should().BeFalse();

        _handler.Fail("/launcher/v2/ping", new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
        (await _client.PingAsync()).Should().BeFalse("an unreachable server is a 'no', not an exception, for ping");
    }

    [Fact]
    public async Task Ping_UsesGet_AndSendsNoBody()
    {
        _handler.Reply("/launcher/v2/ping", """{"Response":"Pong!"}""");

        await _client.PingAsync();

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Path.Should().Be("/launcher/v2/ping");
        request.Body.Should().BeNull();
    }

    [Fact]
    public async Task Changes_SendZlibCompressedJson_WithPut()
    {
        _handler.Reply("/launcher/v2/register", """{"Response":true,"Profiles":[]}""");

        await _client.RegisterAsync("maria", "Left Behind");

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.ContentType.Should().Be("application/json");
        JsonDocument.Parse(request.Body!).RootElement.GetProperty("username").GetString().Should().Be("maria");
        JsonDocument.Parse(request.Body!).RootElement.GetProperty("edition").GetString().Should().Be("Left Behind");
    }

    [Theory]
    [InlineData("login", "/launcher/v2/login")]
    [InlineData("profile", "/launcher/v2/profile")]
    [InlineData("remove", "/launcher/v2/remove")]
    [InlineData("wipe", "/launcher/v2/wipe")]
    public async Task EveryCall_GoesToItsOwnRoute(string call, string route)
    {
        _handler.Reply(route, """{"Response":true,"Profiles":[]}""");

        _ = call switch
        {
            "login" => (object)await _client.LoginAsync("x"),
            "profile" => await _client.GetProfileAsync("x") ?? new object(),
            "remove" => await _client.RemoveAsync("x"),
            _ => await _client.WipeAsync("x", "Standard"),
        };

        _handler.Requests.Single().Path.Should().Be(route);
    }

    [Fact]
    public async Task Login_IsTrueOnlyWhenTheServerSaysTrue()
    {
        _handler.Reply("/launcher/v2/login", """{"Response":true}""");
        (await _client.LoginAsync("yui")).Should().BeTrue();

        _handler.Reply("/launcher/v2/login", """{"Response":false}""");
        (await _client.LoginAsync("nobody")).Should().BeFalse();
    }

    [Fact]
    public async Task Version_Types_AndModPages_AreRead()
    {
        _handler.Reply("/launcher/v2/version", """{"Response":"4.1.6"}""");
        _handler.Reply("/launcher/v2/types", """{"Response":{"Standard":"Basic","Left Behind":"More"}}""");
        _handler.Reply(
            "/launcher/v2/mod-pages",
            """{"Response":[{"name":"Fika","homePage":"https://x","description":"co-op"},{"name":"B","homePage":"https://y"}]}"""
        );

        (await _client.GetVersionAsync()).Should().Be("4.1.6");
        (await _client.GetProfileTypesAsync())
            .Should()
            .Equal(new Dictionary<string, string> { ["Standard"] = "Basic", ["Left Behind"] = "More" });
        (await _client.GetModPagesAsync())
            .Should()
            .Equal(new SptModPage("Fika", "https://x", "co-op"), new SptModPage("B", "https://y", null));
    }

    [Fact]
    public async Task ServerMods_ReadTheShapeTheRealServerAnswers()
    {
        // Captured from a real 4.1.6 server with SVM installed.
        _handler.Reply(
            "/launcher/v2/mods",
            """{"Response":{"SVM":{"ModGuid":"fika.ghostfenixx.svm","Name":"SVM","Author":"GhostFenixx","Contributors":[],"Version":"2.2.3","SptVersion":"~4.1.3","HasPrepatcher":false,"Incompatibilities":[],"ModDependencies":{},"Url":"https://github.com/GhostFenixx","License":"PUSL"}}}"""
        );

        var mods = await _client.GetServerModsAsync();

        mods.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new SptServerMod
                {
                    ModGuid = "fika.ghostfenixx.svm",
                    Name = "SVM",
                    Version = "2.2.3",
                    Author = "GhostFenixx",
                    SptVersion = "~4.1.3",
                }
            );
    }

    [Fact]
    public async Task ServerMods_SkipAnEntryWithoutAGuid_AndTolerateAnEmptyAnswer()
    {
        _handler.Reply("/launcher/v2/mods", """{"Response":{"A":{"Name":"A"},"B":{"ModGuid":"b.b","Name":"B"},"C":"text"}}""");
        (await _client.GetServerModsAsync()).Select(mod => mod.ModGuid).Should().Equal("b.b");

        _handler.Reply("/launcher/v2/mods", """{"Response":{}}""");
        (await _client.GetServerModsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Profiles_ReadTheShapeTheRealServerAnswers_MixedCaseAndAll()
    {
        // The wrapper says "Response", the profile says "profileId": the API mixes both in one answer.
        _handler.Reply(
            "/launcher/v2/profiles",
            """
            {"Response":[{"username":"yui","nickname":"unknown","side":"unknown","currlvl":14,"currexp":0,"prevexp":0,"nextlvl":1000,"maxlvl":78,"edition":"Standard","profileId":"6aeb4ff733d861f39598dace","sptData":{"version":"4.1.6 (40743)","mods":[{"name":"Fixture Client Mod","version":"1.4.2","author":"Someone","dateAdded":1762300000000,"url":"x"},"Bare Name"],"receivedGifts":[]},"wipe":false,"invalidOrUnloadableProfile":false}]}
            """
        );

        var profile = (await _client.GetProfilesAsync()).Should().ContainSingle().Subject;

        profile.Username.Should().Be("yui");
        profile.ProfileId.Should().Be("6aeb4ff733d861f39598dace");
        profile.Edition.Should().Be("Standard");
        profile.Level.Should().Be(14);
        profile.PendingWipe.Should().BeFalse();
        profile.InvalidOrUnloadable.Should().BeFalse();
        profile.SptVersion.Should().Be("4.1.6 (40743)");
        profile
            .ClientMods.Should()
            .Equal(new SptProfileMod("Fixture Client Mod", "1.4.2", "Someone"), new SptProfileMod("Bare Name", null, null));
    }

    [Fact]
    public async Task Profiles_FlagAFreshOrUnloadableProfile()
    {
        _handler.Reply(
            "/launcher/v2/profiles",
            """{"Response":[{"username":"a","profileId":"1","wipe":true},{"username":"b","profileId":"2","invalidOrUnloadableProfile":true}]}"""
        );

        var profiles = await _client.GetProfilesAsync();

        profiles[0].PendingWipe.Should().BeTrue();
        profiles[1].InvalidOrUnloadable.Should().BeTrue();
    }

    [Fact]
    public async Task Register_ReturnsTheVerdictAndTheListAfterwards()
    {
        _handler.Reply("/launcher/v2/register", """{"Response":false,"Profiles":[{"username":"yui","profileId":"1"}]}""");

        var change = await _client.RegisterAsync("yui", "Standard");

        change.Accepted.Should().BeFalse();
        change.Profiles.Should().ContainSingle().Which.Username.Should().Be("yui");
    }

    [Fact]
    public async Task GetProfile_IsNull_WhenTheServerHasNone()
    {
        _handler.Reply("/launcher/v2/profile", """{"Response":null}""");

        (await _client.GetProfileAsync("ghost")).Should().BeNull();
    }

    [Fact]
    public async Task Decode_AcceptsPlainJson_ForServersAndProxiesThatDoNotCompress()
    {
        _handler.ReplyRaw("/launcher/v2/version", Encoding.UTF8.GetBytes("""{"Response":"4.1.6"}"""));

        (await _client.GetVersionAsync()).Should().Be("4.1.6");
    }

    [Fact]
    public async Task Calls_ThrowBadResponse_ForAnswersThatAreNotTheLauncherApi()
    {
        _handler.ReplyRaw("/launcher/v2/version", Encoding.UTF8.GetBytes("<html>nginx</html>"));
        (await FluentActions.Awaiting(() => _client.GetVersionAsync()).Should().ThrowAsync<SptServerException>())
            .Which.Failure.Should()
            .Be(SptServerFailure.BadResponse);

        _handler.ReplyStatus("/launcher/v2/version", HttpStatusCode.NotFound);
        (await FluentActions.Awaiting(() => _client.GetVersionAsync()).Should().ThrowAsync<SptServerException>())
            .Which.Message.Should()
            .Contain("HTTP 404");

        _handler.Reply("/launcher/v2/version", """{"NoResponse":1}""");
        (await FluentActions.Awaiting(() => _client.GetVersionAsync()).Should().ThrowAsync<SptServerException>())
            .Which.Failure.Should()
            .Be(SptServerFailure.BadResponse);
    }

    [Fact]
    public async Task Calls_ThrowUnreachable_WithTheReason()
    {
        _handler.Fail("/launcher/v2/profiles", new HttpRequestException("x", new SocketException((int)SocketError.ConnectionRefused)));

        var failure = (await FluentActions.Awaiting(() => _client.GetProfilesAsync()).Should().ThrowAsync<SptServerException>()).Which;

        failure.Failure.Should().Be(SptServerFailure.Unreachable);
        failure.Message.Should().Contain("https://127.0.0.1:6970").And.Contain("cannot be reached");
    }

    [Fact]
    public async Task Calls_ThrowTimeout_WhenTheServerNeverAnswers()
    {
        _handler.Hang("/launcher/v2/profiles");
        using var impatient = new SptServerClient(new ServerEndpoint("127.0.0.1", 6969), _handler, TimeSpan.FromMilliseconds(80));

        var failure = (await FluentActions.Awaiting(() => impatient.GetProfilesAsync()).Should().ThrowAsync<SptServerException>()).Which;

        failure.Failure.Should().Be(SptServerFailure.Timeout);
    }

    [Fact]
    public async Task Calls_PassOnTheCallersCancellation_AsCancellation()
    {
        _handler.Hang("/launcher/v2/profiles");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await FluentActions.Awaiting(() => _client.GetProfilesAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Factory_CreatesAClientForTheEndpoint()
    {
        using var client = (SptServerClient)new SptServerClientFactory().Create(new ServerEndpoint("10.0.0.5", 7000));

        client.BaseUri.Should().Be(new Uri("https://10.0.0.5:7000"));
    }

    // ---- the real handler, against a real HTTPS server with a certificate nobody trusts ----

    [Fact]
    public async Task TheRealHandler_TalksToAnHttpsServerWithASelfSignedCertificate()
    {
        using var server = WireMock.Server.WireMockServer.Start(new WireMock.Settings.WireMockServerSettings { UseSSL = true, Port = 0 });

        server
            .Given(WireMock.RequestBuilders.Request.Create().WithPath("/launcher/v2/ping").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithBody(Zlib("""{"Response":"Pong!"}""")));
        server
            .Given(WireMock.RequestBuilders.Request.Create().WithPath("/launcher/v2/login").UsingPut())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithBody(Zlib("""{"Response":true}""")));

        var port = new Uri(server.Urls.First(url => url.StartsWith("https", StringComparison.Ordinal))).Port;
        using var client = new SptServerClient(new ServerEndpoint("127.0.0.1", port));

        (await client.PingAsync()).Should().BeTrue("the certificate is self-signed and must be accepted, like the official launcher does");
        (await client.LoginAsync("yui")).Should().BeTrue();

        var login = server.LogEntries.Single(entry => entry.RequestMessage?.Path == "/launcher/v2/login");
        Unzlib(login.RequestMessage!.BodyAsBytes!).Should().Be("""{"username":"yui"}""");
    }

    private sealed record Seen(HttpMethod Method, string Path, string? Body, string? ContentType);

    /// <summary>Plays the server: programmed answers by path, and a record of what it was sent.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<CancellationToken, Task<HttpResponseMessage>>> _routes = [];

        public List<Seen> Requests { get; } = [];

        public void Reply(string path, string json)
        {
            ReplyRaw(path, Zlib(json));
        }

        public void ReplyRaw(string path, byte[] body)
        {
            _routes[path] = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }

        public void ReplyStatus(string path, HttpStatusCode status)
        {
            _routes[path] = _ => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent([]) });
        }

        public void Fail(string path, Exception exception)
        {
            _routes[path] = _ => Task.FromException<HttpResponseMessage>(exception);
        }

        public void Hang(string path)
        {
            _routes[path] = async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = null;

            if (request.Content is not null)
            {
                body = Unzlib(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            }

            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new Seen(request.Method, path, body, request.Content?.Headers.ContentType?.MediaType));

            return await _routes[path](cancellationToken);
        }
    }
}
