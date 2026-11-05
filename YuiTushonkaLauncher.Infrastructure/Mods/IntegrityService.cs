using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Compares what is on disk with the hashes recorded at install time, which is what makes "export the
/// configs the user changed" possible.
/// </summary>
public sealed class IntegrityService(IUnitOfWork unitOfWork, ILogger<IntegrityService> logger) : IIntegrityService
{
    /// <inheritdoc />
    public async Task<IntegrityReport> VerifyAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var (instancePath, mods) = await LoadAsync(instanceId, cancellationToken);

        var results = new List<ModIntegrity>(mods.Count);
        foreach (var mod in mods)
        {
            results.Add(VerifyMod(instancePath, mod));
        }

        logger.LogInformation(
            "Integrity of instance {InstanceId}: {Mods} mod(s), {Drifted} drifted file(s)",
            instanceId,
            results.Count,
            results.Sum(result => result.Drifted.Count)
        );

        return new IntegrityReport { InstanceId = instanceId, Mods = results };
    }

    /// <inheritdoc />
    public async Task<ModIntegrity?> VerifyModAsync(Guid instanceId, string modGuid, CancellationToken cancellationToken = default)
    {
        var (instancePath, mods) = await LoadAsync(instanceId, cancellationToken);
        var mod = mods.FirstOrDefault(item => string.Equals(item.Guid, modGuid, StringComparison.OrdinalIgnoreCase));

        return mod is null ? null : VerifyMod(instancePath, mod);
    }

    private async Task<(string InstancePath, List<Core.Domain.InstalledMod> Mods)> LoadAsync(
        Guid instanceId,
        CancellationToken cancellationToken
    )
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instance = await context
                    .Instances.Include(row => row.InstalledMods)
                        .ThenInclude(mod => mod.Files)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token);

                return instance is null
                    ? throw new InstanceValidationException([$"Instance {instanceId} was not found."])
                    : (instance.Path, instance.InstalledMods.ToList());
            },
            cancellationToken
        );
    }

    private static ModIntegrity VerifyMod(string instancePath, Core.Domain.InstalledMod mod)
    {
        var drifted = new List<FileDrift>();
        var modifiedConfigs = new List<string>();

        foreach (var file in mod.Files)
        {
            var path = Path.Combine(instancePath, file.RelativePath);

            if (!File.Exists(path))
            {
                drifted.Add(new FileDrift(file.RelativePath, FileDriftKind.Missing, file.Length, null));
                continue;
            }

            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                continue;
            }

            if (length != file.Length || !HashMatches(path, file.Sha256))
            {
                drifted.Add(new FileDrift(file.RelativePath, FileDriftKind.Modified, file.Length, length));

                if (file.IsConfig)
                {
                    modifiedConfigs.Add(file.RelativePath);
                }
            }
        }

        return new ModIntegrity
        {
            ModGuid = mod.Guid,
            Name = mod.Name,
            Drifted = drifted,
            ModifiedConfigs = modifiedConfigs,
        };
    }

    private static bool HashMatches(string path, string expectedSha256)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return string.Equals(Convert.ToHexStringLower(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
