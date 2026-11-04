using System.Text.Json;
using System.Text.Json.Nodes;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>Reads and edits the keys of the server's <c>http.json</c> the launcher cares about, leaving every other key as it was.</summary>
internal static class ServerHttpConfig
{
    public const string DefaultHost = "127.0.0.1";

    /// <summary>The address and port, or why the file cannot be used.</summary>
    public static (ServerEndpoint? Endpoint, string? Error) Read(string path)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"'{path}' cannot be read: {exception.Message}");
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            return (null, $"'{path}' is not valid JSON: {exception.Message}");
        }

        if (root is not JsonObject config)
        {
            return (null, $"'{path}' does not contain a JSON object.");
        }

        var host =
            config["ip"] is JsonValue ipValue && ipValue.TryGetValue<string>(out var ip) && !string.IsNullOrWhiteSpace(ip)
                ? ip
                : DefaultHost;
        var port = ServerEndpoint.DefaultPort;

        if (config["port"] is { } portNode)
        {
            if (portNode is not JsonValue portValue || !portValue.TryGetValue<int>(out port))
            {
                return (null, $"'port' in '{path}' is not a number.");
            }
        }

        if (port is < 1 or > 65535)
        {
            return (null, $"'port' in '{path}' is {port}, which is not a valid TCP port.");
        }

        return (new ServerEndpoint(host, port), null);
    }

    /// <summary>The document with <c>port</c> (and <c>backendPort</c>, when it followed it) set to <paramref name="port" />.</summary>
    public static string WithPort(string path, int port)
    {
        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });

        if (root is not JsonObject config)
        {
            throw new InvalidDataException($"'{path}' does not contain a JSON object.");
        }

        // backendPort is the port clients are told to use. If it matched the listening port, it must keep matching;
        // if someone made them differ on purpose (a reverse proxy), that is theirs to keep.
        var followsPort =
            config["backendPort"] is not { } backend
            || (backend is JsonValue value && value.TryGetValue<int>(out var current) && current == ReadPort(config));

        config["port"] = port;

        if (followsPort)
        {
            config["backendPort"] = port;
        }

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static int ReadPort(JsonObject config)
    {
        return config["port"] is JsonValue value && value.TryGetValue<int>(out var port) ? port : ServerEndpoint.DefaultPort;
    }
}
