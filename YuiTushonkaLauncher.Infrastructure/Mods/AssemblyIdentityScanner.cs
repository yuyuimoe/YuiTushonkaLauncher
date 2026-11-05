using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Mods;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Reads mod identity straight out of a managed assembly's metadata.
/// </summary>
/// <remarks>
/// <para>
/// Client mods are BepInEx plugins: the identity lives in the <c>[BepInPlugin(guid, name, version)]</c>
/// custom attribute, so the value is a constant and can be read without executing anything.
/// </para>
/// <para>
/// Server mods implement <c>IModMetadata</c>, whose values are compiled into the type (property
/// initialisers and the constructor become <c>ldstr</c> instructions — verified against
/// <c>DynamicMaps.ModMetadata</c>). The reader therefore scans the IL of the metadata type for string
/// literals and picks the GUID by its reverse-DNS shape. No mod code is loaded or executed; the
/// trade-off is that a mod building its GUID at run time stays unrecognised, which is exactly what
/// <see cref="ModRecognition.GuidMissing" /> is for.
/// </para>
/// </remarks>
internal static class AssemblyIdentityScanner
{
    private const string BepInPluginAttribute = "BepInPlugin";
    private const string ServerMetadataInterface = "IModMetadata";

    /// <summary>Reads a BepInEx plugin identity from the assembly.</summary>
    public static ModIdentity? TryReadBepInPlugin(string assemblyPath)
    {
        foreach (var typeName in new[] { BepInPluginAttribute })
        {
            var values = TryReadAttribute(typeName, assemblyPath);
            if (values is not { Count: >= 3 })
            {
                continue;
            }

            return new ModIdentity
            {
                Guid = NullIfEmpty(values[0]),
                Name = NullIfEmpty(values[1]),
                Version = Core.Domain.SemanticVersion.TryParse(values[2], out var version) ? version : null,
                Kind = ModKind.Client,
                Source = ModIdentitySource.BepInPluginAttribute,
                SourcePath = assemblyPath,
            };
        }

        return null;
    }

    /// <summary>Reads an <c>IModMetadata</c> identity from the assembly's string literals.</summary>
    public static ModIdentity? TryReadServerMetadata(string assemblyPath)
    {
        var literals = TryReadMetadataLiterals(assemblyPath);
        if (literals is null || literals.Count == 0)
        {
            return null;
        }

        string? guid = null;
        Core.Domain.SemanticVersion? version = null;

        foreach (var literal in literals)
        {
            if (guid is null && LooksLikeGuid(literal))
            {
                guid = literal;
                continue;
            }

            if (version is null && Core.Domain.SemanticVersion.TryParse(literal, out var parsed) && literal.Contains('.'))
            {
                version = parsed;
            }
        }

        if (guid is null)
        {
            return null;
        }

        return new ModIdentity
        {
            Guid = guid,
            Version = version,
            Kind = ModKind.Server,
            Source = ModIdentitySource.ServerModMetadata,
            SourcePath = assemblyPath,
        };
    }

    /// <summary>Reads the fixed string arguments of one custom attribute, when present.</summary>
    private static List<string?>? TryReadAttribute(string attributeTypeName, string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
            {
                return null;
            }

            var reader = peReader.GetMetadataReader();

            foreach (var handle in reader.CustomAttributes)
            {
                var attribute = reader.GetCustomAttribute(handle);
                var name = GetAttributeTypeName(reader, attribute.Constructor);

                // The metadata name carries the "Attribute" suffix; the C# usage does not.
                if (
                    name is null
                    || !(
                        string.Equals(name, attributeTypeName, StringComparison.Ordinal)
                        || string.Equals(name, attributeTypeName + "Attribute", StringComparison.Ordinal)
                    )
                )
                {
                    continue;
                }

                return ReadFixedStringArguments(reader, attribute);
            }
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static List<string?> ReadFixedStringArguments(MetadataReader reader, CustomAttribute attribute)
    {
        var blob = reader.GetBlobReader(attribute.Value);
        var arguments = new List<string?>();

        // Prolog of a custom attribute blob.
        if (blob.ReadUInt16() != 1)
        {
            return arguments;
        }

        try
        {
            while (blob.RemainingBytes > 0)
            {
                var value = blob.ReadSerializedString();
                arguments.Add(value);
            }
        }
        catch (BadImageFormatException)
        {
            // A malformed blob simply yields what was readable so far.
        }

        return arguments;
    }

    /// <summary>Collects the string literals of the type implementing <c>IModMetadata</c>.</summary>
    private static List<string>? TryReadMetadataLiterals(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
            {
                return null;
            }

            var reader = peReader.GetMetadataReader();
            var metadataTypes = FindMetadataTypes(reader);
            if (metadataTypes.Count == 0)
            {
                return null;
            }

            var literals = new List<string>();

            foreach (var typeHandle in metadataTypes)
            {
                var type = reader.GetTypeDefinition(typeHandle);

                foreach (var methodHandle in type.GetMethods())
                {
                    literals.AddRange(ReadStringLiterals(peReader, reader, reader.GetMethodDefinition(methodHandle)));
                }
            }

            return literals;
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<TypeDefinitionHandle> FindMetadataTypes(MetadataReader reader)
    {
        var matches = new List<TypeDefinitionHandle>();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);

            foreach (var implementationHandle in type.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                var name = GetTypeName(reader, implementation.Interface);

                if (string.Equals(name, ServerMetadataInterface, StringComparison.Ordinal))
                {
                    matches.Add(typeHandle);
                    break;
                }
            }
        }

        return matches;
    }

    private static IEnumerable<string> ReadStringLiterals(PEReader peReader, MetadataReader reader, MethodDefinition method)
    {
        if (method.RelativeVirtualAddress == 0)
        {
            return [];
        }

        var literals = new List<string>();

        try
        {
            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            var il = body.GetILBytes() ?? [];

            for (var index = 0; index + 5 <= il.Length; index++)
            {
                // 0x72 = ldstr <token>. Only tokens pointing into the user-string heap are literals.
                if (il[index] != 0x72)
                {
                    continue;
                }

                var token = BitConverter.ToInt32(il, index + 1);
                if ((token >> 24) != 0x70)
                {
                    continue;
                }

                var offset = token & 0x00FF_FFFF;
                if (offset <= 0)
                {
                    continue;
                }

                string literal;
                try
                {
                    literal = reader.GetUserString(MetadataTokens.UserStringHandle(offset));
                }
                catch (BadImageFormatException)
                {
                    // The token does not point at a valid string; skip it.
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(literal) && literal.All(character => !char.IsControl(character)))
                {
                    literals.Add(literal);
                }

                index += 4;
            }
        }
        catch (BadImageFormatException)
        {
            // Unreadable IL is treated as "no literals".
        }

        return literals;
    }

    private static string? GetAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        return constructor.Kind switch
        {
            HandleKind.MemberReference => GetTypeName(reader, reader.GetMemberReference((MemberReferenceHandle)constructor).Parent),
            HandleKind.MethodDefinition => GetTypeName(
                reader,
                reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()
            ),
            _ => null,
        };
    }

    private static string? GetTypeName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
            HandleKind.TypeSpecification => null,
            _ => null,
        };
    }

    private static bool LooksLikeGuid(string value)
    {
        // Reverse-DNS shape: at least one dot, no spaces, no path separators, no version-like start.
        if (value.Length is < 3 or > 200 || value.Contains(' ') || value.Contains('/') || value.Contains('\\'))
        {
            return false;
        }

        var segments = value.Split('.');
        if (segments.Length < 2)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return false;
            }

            if (segment.All(char.IsAsciiDigit))
            {
                // "1.2.1" is a version, not a GUID.
                return false;
            }
        }

        return true;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
