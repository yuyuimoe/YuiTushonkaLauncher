using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YuiTushonkaLauncher.Core.Packs;

namespace YuiTushonkaLauncher.Infrastructure.Packs;

/// <summary>
/// Reads and writes <c>manifest.json</c> in one canonical form: sorted keys, two-space indent, <c>\n</c>
/// line endings, no BOM. The same composition always produces the same bytes, which is what makes the
/// share code stable.
/// </summary>
public static class PackManifestSerializer
{
    /// <summary>Properties that describe the export, not the composition; they never enter the share code.</summary>
    private static readonly string[] ExportMetadata = ["createdAt", "appVersion", "instanceName", "shareCode"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // Format 1 is closed: an unknown member is an error, not something to guess about. A *newer*
        // format is detected before this strictness is applied (see <see cref="ReadFormatVersion" />).
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Serialises a manifest to its canonical bytes.</summary>
    public static byte[] Serialize(PackManifest manifest)
    {
        var node = JsonSerializer.SerializeToNode(manifest, Options)!;

        return Write(Canonicalize(node), indented: true, trailingNewline: true);
    }

    /// <summary>
    /// Computes the share code of the composition in <paramref name="manifest" />: everything that decides what
    /// gets installed, and nothing that only describes the export (timestamps, instance name, display names).
    /// </summary>
    public static string ComputeShareCode(PackManifest manifest)
    {
        var node = (JsonObject)JsonSerializer.SerializeToNode(manifest, Options)!;
        foreach (var name in ExportMetadata)
        {
            node.Remove(name);
        }

        // Display names say nothing about what is installed (guid, version and content digest do), and they
        // depend on how the exporting launcher happened to name a mod, so they must not change the code.
        foreach (var list in new[] { "mods", "addons" })
        {
            if (node[list] is JsonArray entries)
            {
                foreach (var entry in entries.OfType<JsonObject>())
                {
                    entry.Remove("name");
                }
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Write(Canonicalize(node), indented: false, trailingNewline: false)));
    }

    /// <summary>
    /// Reads only <c>formatVersion</c>, tolerating anything else in the document, so a package from a newer
    /// launcher gets an upgrade hint instead of a schema error.
    /// </summary>
    /// <returns>The declared version, or <c>null</c> when the field is missing or not an integer.</returns>
    public static int? ReadFormatVersion(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());

            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("formatVersion", out var version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses a manifest with the strict schema.</summary>
    /// <exception cref="JsonException">The document does not match the schema.</exception>
    public static PackManifest Deserialize(ReadOnlySpan<byte> json)
    {
        return JsonSerializer.Deserialize<PackManifest>(json, Options) ?? throw new JsonException("The manifest is empty.");
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                var sorted = new JsonObject();
                foreach (var pair in jsonObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    sorted.Add(pair.Key, Canonicalize(pair.Value));
                }

                return sorted;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(Canonicalize(item));
                }

                return items;
            default:
                return node?.DeepClone();
        }
    }

    private static byte[] Write(JsonNode? node, bool indented, bool trailingNewline)
    {
        using var stream = new MemoryStream();

        using (
            var writer = new Utf8JsonWriter(
                stream,
                new JsonWriterOptions
                {
                    Indented = indented,
                    NewLine = "\n",

                    // The file is not HTML: readable output beats escaping '+' in a version string.
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            )
        )
        {
            node!.WriteTo(writer);
        }

        if (trailingNewline)
        {
            stream.WriteByte((byte)'\n');
        }

        return stream.ToArray();
    }
}
