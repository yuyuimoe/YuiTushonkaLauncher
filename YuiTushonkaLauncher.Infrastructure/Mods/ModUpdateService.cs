using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Combines the Forge update check with local state: pins, what is installed, and the instance's SPT
/// version. The API decides what is compatible; this service decides what the user actually wants to see.
/// </summary>
public sealed class ModUpdateService(IUnitOfWork unitOfWork, IForgeClient forge, ILogger<ModUpdateService> logger) : IModUpdateService
{
    /// <inheritdoc />
    public async Task<ModUpdatePlan> CheckAsync(Guid instanceId, bool includePinned = false, CancellationToken cancellationToken = default)
    {
        var (installed, sptVersion) = await LoadAsync(instanceId, cancellationToken);

        if (installed.Count == 0)
        {
            return new ModUpdatePlan { SptVersion = sptVersion };
        }

        var packages = installed.Select(mod => new InstalledPackage { Guid = mod.Guid, Version = mod.Version }).ToList();
        var report = await forge.CheckUpdatesAsync(packages, sptVersion, cancellationToken);

        var statuses = new List<ModUpdateStatus>(installed.Count);

        foreach (var mod in installed)
        {
            var update = report.Updates.FirstOrDefault(candidate =>
                string.Equals(candidate.Current.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase)
            );
            var blocked = report.BlockedUpdates.FirstOrDefault(candidate =>
                string.Equals(candidate.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase)
            );
            var incompatible = report.Incompatible.FirstOrDefault(candidate =>
                string.Equals(candidate.Current.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase)
            );

            var status = new ModUpdateStatus
            {
                Guid = mod.Guid,
                Name = mod.Name,
                InstalledVersion = mod.Version,
                Decision = UpdateDecision.UpToDate,
            };

            if (mod.IsPinned && !includePinned)
            {
                status = status with
                {
                    AvailableVersion = update?.Recommended.Version,
                    Decision = UpdateDecision.Pinned,
                    Reason = "Pinned by the user; automatic updates skip it.",
                };
            }
            else if (update is not null)
            {
                status = status with
                {
                    AvailableVersion = update.Recommended.Version,
                    Decision = UpdateDecision.Update,
                    DownloadUrl = update.Recommended.DownloadUrl,
                    ContentLength = update.Recommended.ContentLength,
                };
            }
            else if (blocked is not null)
            {
                status = status with { Decision = UpdateDecision.Blocked, Reason = "The Forge API holds this update back." };
            }
            else if (incompatible is not null)
            {
                status = status with
                {
                    AvailableVersion = incompatible.LatestCompatibleVersion,
                    Decision = UpdateDecision.Incompatible,
                    Reason = $"No version fits SPT {sptVersion} ({incompatible.Reason}).",
                };
            }

            statuses.Add(status);
        }

        logger.LogInformation(
            "Update check for instance {InstanceId} (SPT {SptVersion}): {Updatable} updatable of {Total} mod(s)",
            instanceId,
            sptVersion,
            statuses.Count(status => status.Decision == UpdateDecision.Update),
            statuses.Count
        );

        return new ModUpdatePlan { SptVersion = sptVersion, Statuses = statuses };
    }

    private async Task<(List<InstalledMod> Installed, SemanticVersion SptVersion)> LoadAsync(
        Guid instanceId,
        CancellationToken cancellationToken
    )
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instance = await context
                    .Instances.Include(row => row.InstalledMods)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token);

                return instance is null
                    ? throw new InstanceValidationException([$"Instance {instanceId} was not found."])
                    : (instance.InstalledMods.Where(mod => mod.ParentModGuid is null).ToList(), instance.SptVersion);
            },
            cancellationToken
        );
    }
}
