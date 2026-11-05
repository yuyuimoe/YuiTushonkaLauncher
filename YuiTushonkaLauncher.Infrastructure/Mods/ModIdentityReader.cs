using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Mods;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Reads mod identity by trying the two declarations that exist in the ecosystem: the BepInEx plugin
/// attribute (client) and an <c>IModMetadata</c> implementation (server).
/// </summary>
public sealed class ModIdentityReader(ILogger<ModIdentityReader> logger) : IModIdentityReader
{
    /// <inheritdoc />
    public ModIdentity Read(string assemblyPath, ModKind expectedKind)
    {
        var clientIdentity = AssemblyIdentityScanner.TryReadBepInPlugin(assemblyPath);
        if (clientIdentity is not null)
        {
            logger.LogDebug("Read BepInPlugin identity {Guid} from {Path}", clientIdentity.Guid, assemblyPath);
            return clientIdentity;
        }

        var serverIdentity = AssemblyIdentityScanner.TryReadServerMetadata(assemblyPath);
        if (serverIdentity is not null)
        {
            logger.LogDebug("Read IModMetadata identity {Guid} from {Path}", serverIdentity.Guid, assemblyPath);
            return serverIdentity;
        }

        logger.LogDebug("No mod identity found in {Path}", assemblyPath);

        return new ModIdentity
        {
            Kind = expectedKind,
            Source = ModIdentitySource.None,
            SourcePath = assemblyPath,
            Reason = "No BepInPlugin attribute or IModMetadata type was found in the assembly.",
        };
    }
}
