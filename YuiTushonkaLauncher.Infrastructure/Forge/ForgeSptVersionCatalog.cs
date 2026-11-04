using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// Keeps the local <see cref="SptRelease" /> table in sync with <c>/spt/versions</c>. The table is what
/// the SPT↔Tarkov compatibility matrix reads, so it must not depend on a live API call.
/// </summary>
public sealed class ForgeSptVersionCatalog(IForgeClient forge, IUnitOfWork unitOfWork, ILogger<ForgeSptVersionCatalog> logger)
{
    /// <summary>Fetches the catalogue and upserts it, returning the stored rows (newest first).</summary>
    public async Task<IReadOnlyList<SptRelease>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var remote = await forge.GetSptVersionsAsync(perPage: 100, cancellationToken);
        if (remote.Count == 0)
        {
            logger.LogWarning("The Forge API returned no SPT versions; keeping the cached catalogue");
            return await ReadAsync(cancellationToken);
        }

        var newest = remote.Max(release => release.Version);

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var existing = await context.SptReleases.ToDictionaryAsync(row => row.Version.ToString(), token);

                foreach (var release in remote)
                {
                    var key = release.Version.ToString();
                    if (existing.TryGetValue(key, out var row))
                    {
                        row.ForgeId = release.ForgeId;
                        row.ModCount = release.ModCount;
                        row.ReleaseUrl = release.ReleaseUrl;
                        row.IsCurrent = release.Version == newest;
                        row.FetchedAt = DateTimeOffset.UtcNow;
                        continue;
                    }

                    context.SptReleases.Add(
                        new SptRelease
                        {
                            Id = Guid.NewGuid(),
                            ForgeId = release.ForgeId,
                            Version = release.Version,
                            ModCount = release.ModCount,
                            ReleaseUrl = release.ReleaseUrl,
                            IsCurrent = release.Version == newest,
                            FetchedAt = DateTimeOffset.UtcNow,
                        }
                    );
                }

                // Only one entry may be flagged current.
                foreach (var row in existing.Values.Where(row => row.IsCurrent && row.Version != newest))
                {
                    row.IsCurrent = false;
                }
            },
            cancellationToken
        );

        logger.LogInformation("Synced {Count} SPT release(s); current is {Current}", remote.Count, newest);
        return await ReadAsync(cancellationToken);
    }

    /// <summary>Reads the cached catalogue without touching the network.</summary>
    public async Task<IReadOnlyList<SptRelease>> ReadAsync(CancellationToken cancellationToken = default)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) => (IReadOnlyList<SptRelease>)await context.SptReleases.AsNoTracking().ToListAsync(token),
            cancellationToken
        );
    }
}
