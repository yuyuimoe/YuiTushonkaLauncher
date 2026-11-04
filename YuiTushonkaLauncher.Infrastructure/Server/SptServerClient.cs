using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// The launcher API of one SPT server. The wire format, measured against a real 4.1.6 server: HTTPS with a
/// self-signed certificate; every body, in both directions, is zlib-compressed JSON; reads are GET, changes are PUT.
/// </summary>
public sealed class SptServerClient : ISptServerClient, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    public SptServerClient(ServerEndpoint endpoint, HttpMessageHandler? handler = null)
    {
        BaseUri = endpoint.BaseUri;
        _http = new HttpClient(handler ?? CreateHandler()) { BaseAddress = BaseUri, Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <inheritdoc />
    public Uri BaseUri { get; }

    /// <summary>
    /// A handler that accepts the server's self-signed certificate. SPT creates its own certificate on first
    /// start, so there is nothing to verify it against; the same is true of the official launcher. No secret
    /// is ever sent: a profile is selected by its name alone.
    /// </summary>
    public static HttpMessageHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
    }

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var root = await SendAsync(HttpMethod.Get, "/launcher/v2/ping", null, cancellationToken);
            return Field(root, "Response") is { ValueKind: JsonValueKind.String } response && response.GetString() == "Pong!";
        }
        catch (SptServerException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "/launcher/v2/version", null, cancellationToken);
        return Field(root, "Response")?.GetString() ?? throw BadResponse("/launcher/v2/version", "no 'Response' string");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetProfileTypesAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "/launcher/v2/types", null, cancellationToken);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Field(root, "Response") is { ValueKind: JsonValueKind.Object } response)
        {
            foreach (var property in response.EnumerateObject())
            {
                types[property.Name] =
                    property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : string.Empty;
            }
        }

        return types;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SptProfileSummary>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "/launcher/v2/profiles", null, cancellationToken);
        return ReadProfiles(Field(root, "Response"));
    }

    /// <inheritdoc />
    public async Task<SptProfileSummary?> GetProfileAsync(string username, CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Put, "/launcher/v2/profile", new { username }, cancellationToken);
        return Field(root, "Response") is { ValueKind: JsonValueKind.Object } profile ? ReadProfile(profile) : null;
    }

    /// <inheritdoc />
    public async Task<bool> LoginAsync(string username, CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Put, "/launcher/v2/login", new { username }, cancellationToken);
        return Field(root, "Response") is { ValueKind: JsonValueKind.True };
    }

    /// <inheritdoc />
    public Task<SptProfileChange> RegisterAsync(string username, string edition, CancellationToken cancellationToken = default)
    {
        return ChangeAsync("/launcher/v2/register", new { username, edition }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SptProfileChange> RemoveAsync(string username, CancellationToken cancellationToken = default)
    {
        return ChangeAsync("/launcher/v2/remove", new { username }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SptProfileChange> WipeAsync(string username, string edition, CancellationToken cancellationToken = default)
    {
        return ChangeAsync("/launcher/v2/wipe", new { username, edition }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SptServerMod>> GetServerModsAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "/launcher/v2/mods", null, cancellationToken);
        var mods = new List<SptServerMod>();

        if (Field(root, "Response") is { ValueKind: JsonValueKind.Object } response)
        {
            foreach (var entry in response.EnumerateObject().Where(entry => entry.Value.ValueKind == JsonValueKind.Object))
            {
                var guid = Text(entry.Value, "ModGuid");

                if (guid is not null)
                {
                    mods.Add(
                        new SptServerMod
                        {
                            ModGuid = guid,
                            Name = Text(entry.Value, "Name") ?? entry.Name,
                            Version = Text(entry.Value, "Version"),
                            Author = Text(entry.Value, "Author"),
                            SptVersion = Text(entry.Value, "SptVersion"),
                        }
                    );
                }
            }
        }

        return mods;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SptModPage>> GetModPagesAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "/launcher/v2/mod-pages", null, cancellationToken);
        var pages = new List<SptModPage>();

        if (Field(root, "Response") is { ValueKind: JsonValueKind.Array } response)
        {
            foreach (var page in response.EnumerateArray().Where(page => page.ValueKind == JsonValueKind.Object))
            {
                pages.Add(
                    new SptModPage(Text(page, "name") ?? string.Empty, Text(page, "homePage") ?? string.Empty, Text(page, "description"))
                );
            }
        }

        return pages;
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private async Task<SptProfileChange> ChangeAsync(string path, object body, CancellationToken cancellationToken)
    {
        var root = await SendAsync(HttpMethod.Put, path, body, cancellationToken);
        var accepted = Field(root, "Response") is { ValueKind: JsonValueKind.True };
        return new SptProfileChange(accepted, ReadProfiles(Field(root, "Profiles")));
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = new ByteArrayContent(Compress(JsonSerializer.Serialize(body)));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw BadResponse(path, $"HTTP {(int)response.StatusCode}");
            }

            return Decode(path, bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SptServerException(
                SptServerFailure.Timeout,
                $"{BaseUri} did not answer '{path}' within {RequestTimeout.TotalSeconds:0} seconds."
            );
        }
        catch (HttpRequestException exception)
        {
            var reason = exception.InnerException is SocketException socket ? socket.Message : exception.Message;
            throw new SptServerException(SptServerFailure.Unreachable, $"{BaseUri} cannot be reached: {reason}", exception);
        }
    }

    private JsonElement Decode(string path, byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflater.CopyTo(output);
            return JsonDocument.Parse(output.ToArray()).RootElement.Clone();
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            // Not zlib: some servers (or proxies) may answer plain JSON; anything else is not the launcher API.
            try
            {
                return JsonDocument.Parse(bytes).RootElement.Clone();
            }
            catch (JsonException)
            {
                throw BadResponse(path, "the body is neither zlib-compressed JSON nor JSON", exception);
            }
        }
    }

    private static byte[] Compress(string json)
    {
        using var output = new MemoryStream();

        using (var deflater = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflater.Write(Encoding.UTF8.GetBytes(json));
        }

        return output.ToArray();
    }

    private SptServerException BadResponse(string path, string detail, Exception? inner = null)
    {
        return new SptServerException(
            SptServerFailure.BadResponse,
            $"{BaseUri} answered '{path}' with something that is not the SPT launcher API: {detail}.",
            inner
        );
    }

    private static IReadOnlyList<SptProfileSummary> ReadProfiles(JsonElement? element)
    {
        var profiles = new List<SptProfileSummary>();

        if (element is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var profile in array.EnumerateArray().Where(profile => profile.ValueKind == JsonValueKind.Object))
            {
                profiles.Add(ReadProfile(profile));
            }
        }

        return profiles;
    }

    private static SptProfileSummary ReadProfile(JsonElement profile)
    {
        var data = Field(profile, "sptData");
        var mods = new List<SptProfileMod>();

        if (data is { ValueKind: JsonValueKind.Object } && Field(data.Value, "mods") is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var mod in list.EnumerateArray())
            {
                if (mod.ValueKind == JsonValueKind.Object && Text(mod, "name") is { } name)
                {
                    mods.Add(new SptProfileMod(name, Text(mod, "version"), Text(mod, "author")));
                }
                else if (mod.ValueKind == JsonValueKind.String && mod.GetString() is { Length: > 0 } text)
                {
                    mods.Add(new SptProfileMod(text, null, null));
                }
            }
        }

        return new SptProfileSummary
        {
            Username = Text(profile, "username") ?? string.Empty,
            Nickname = Text(profile, "nickname") ?? string.Empty,
            Side = Text(profile, "side") ?? string.Empty,
            Level = Field(profile, "currlvl") is { ValueKind: JsonValueKind.Number } level && level.TryGetInt32(out var value) ? value : 0,
            ProfileId = Text(profile, "profileId") ?? string.Empty,
            Edition = Text(profile, "edition") ?? string.Empty,
            InvalidOrUnloadable = Field(profile, "invalidOrUnloadableProfile") is { ValueKind: JsonValueKind.True },
            PendingWipe = Field(profile, "wipe") is { ValueKind: JsonValueKind.True },
            SptVersion = data is { ValueKind: JsonValueKind.Object } ? Text(data.Value, "version") : null,
            ClientMods = mods,
        };
    }

    /// <summary>A property by name, ignoring case: the API mixes <c>Response</c> and <c>profileId</c> in one answer.</summary>
    private static JsonElement? Field(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? Text(JsonElement element, string name)
    {
        return Field(element, name) is { ValueKind: JsonValueKind.String } value && value.GetString() is { Length: > 0 } text ? text : null;
    }
}

/// <summary>Creates a client for each endpoint it is asked about.</summary>
public sealed class SptServerClientFactory : ISptServerClientFactory
{
    /// <inheritdoc />
    public ISptServerClient Create(ServerEndpoint endpoint)
    {
        return new SptServerClient(endpoint);
    }
}
