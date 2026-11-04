namespace YuiTushonkaLauncher.Core.Server;

/// <summary>Where an SPT server listens: the address it binds to and its port.</summary>
/// <param name="Host">The bind address from <c>http.json</c>, or the host of a remote server.</param>
/// <param name="Port">TCP port.</param>
public sealed partial record ServerEndpoint(string Host, int Port)
{
    /// <summary>
    /// The address a client connects to. A server bound to <c>0.0.0.0</c> or <c>::</c> listens everywhere,
    /// but those are not addresses one can connect to, so they map to loopback.
    /// </summary>
    public string ConnectHost
    {
        get
        {
            return Host.Trim() switch
            {
                "" or "0.0.0.0" or "::" or "[::]" => "127.0.0.1",
                var host when host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') => $"[{host}]",
                var host => host,
            };
        }
    }

    /// <summary>The base URL of the launcher API. SPT serves HTTPS only.</summary>
    public Uri BaseUri
    {
        get { return new Uri($"https://{ConnectHost}:{Port}"); }
    }

    /// <summary>True when the server accepts connections from other machines (bound to every interface).</summary>
    public bool ListensOnAllInterfaces
    {
        get { return Host.Trim() is "" or "0.0.0.0" or "::" or "[::]"; }
    }

    /// <summary>Parses <c>host:port</c>, <c>https://host:port</c> or a bare host (default port 6969).</summary>
    public static bool TryParse(string text, out ServerEndpoint endpoint)
    {
        endpoint = new ServerEndpoint("127.0.0.1", DefaultPort);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        candidate = candidate.Contains("://", StringComparison.Ordinal) ? candidate : "https://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        // Uri reports 443 for "no port", which would hide the SPT default.
        var explicitPort = ExplicitPort().Match(candidate);
        var port = explicitPort.Success
            ? int.Parse(explicitPort.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : DefaultPort;

        if (port is < 1 or > 65535)
        {
            return false;
        }

        endpoint = new ServerEndpoint(uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host, port);
        return true;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^https://(?:\[[^\]]+\]|[^/:?#]+):(\d{1,5})(?:[/?#]|$)")]
    private static partial System.Text.RegularExpressions.Regex ExplicitPort();

    /// <summary>The port SPT uses unless <c>http.json</c> says otherwise.</summary>
    public const int DefaultPort = 6969;
}
