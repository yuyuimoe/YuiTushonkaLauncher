using Microsoft.EntityFrameworkCore;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Compares an instance with what a server runs and installs the exact versions the server has. The server's list
/// is its <c>/launcher/v2/mods</c> answer (the server mods it loaded); client mods are only compared, because the
/// server learns them from the game and does not publish them. Pins are honoured: a pin is a promise.
/// </summary>
public sealed class ServerSyncService(
    IUnitOfWork unitOfWork,
    ISptServerLocator locator,
    ISptServerClientFactory clients,
    IModInstaller installer,
    IForgeClient forge
) : IServerSyncService
{
    /// <inheritdoc />
    public async Task<ServerSyncPlan> PlanAsync(
        Guid instanceId,
        ServerEndpoint? server = null,
        CancellationToken cancellationToken = default
    )
    {
        var (instance, lastProfileId) = await LoadAsync(instanceId, cancellationToken);
        var endpoint = server ?? locator.Locate(instance).Endpoint;
        var client = clients.Create(endpoint);

        try
        {
            if (!await client.PingAsync(cancellationToken))
            {
                throw new ServerNotRunningException($"No SPT server answers at {endpoint.BaseUri}.");
            }

            var serverMods = await client.GetServerModsAsync(cancellationToken);
            var version = await client.GetVersionAsync(cancellationToken);
            var clientMods = await ReadClientModsAsync(client, lastProfileId, cancellationToken);

            return new ServerSyncPlan
            {
                Server = endpoint,
                ServerSptVersion = version,
                ServerMods = CompareServerMods(instance, serverMods),
                ClientMods = CompareClientMods(instance, clientMods),
            };
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<ServerSyncResult> ApplyAsync(
        Guid instanceId,
        ServerSyncPlan plan,
        ServerSyncOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new ServerSyncOptions();
        var outcomes = new List<ServerSyncOutcome>();

        var targets = plan.ServerMods.Where(entry =>
            entry.Status is ServerSyncStatus.Missing or ServerSyncStatus.VersionDiffers
            || entry is { Status: ServerSyncStatus.PinnedDiffers } && options.OverridePins
        );

        foreach (var entry in targets)
        {
            try
            {
                await InstallExactAsync(instanceId, entry, cancellationToken);
                outcomes.Add(new ServerSyncOutcome(entry, true, null));
            }
            catch (Exception exception)
                when (exception is ForgeException or InvalidOperationException or InvalidDataException or IOException)
            {
                outcomes.Add(new ServerSyncOutcome(entry, false, exception.Message));
            }
        }

        foreach (var skipped in plan.ServerMods.Where(entry => entry.Status == ServerSyncStatus.PinnedDiffers && !options.OverridePins))
        {
            outcomes.Add(new ServerSyncOutcome(skipped, false, "Pinned: left at the installed version. Pass the override to change it."));
        }

        return new ServerSyncResult(outcomes);
    }

    private async Task InstallExactAsync(Guid instanceId, ServerSyncEntry entry, CancellationToken cancellationToken)
    {
        if (!SemanticVersion.TryParse(entry.ServerVersion, out var wanted))
        {
            throw new InvalidDataException($"The server reports '{entry.ServerVersion}' for '{entry.Name}', which is not a version.");
        }

        var found = (await forge.FindModsByGuidsAsync([entry.Guid], cancellationToken)).FirstOrDefault(mod =>
            string.Equals(mod.Guid, entry.Guid, StringComparison.OrdinalIgnoreCase)
        );

        if (found is null)
        {
            throw new InvalidOperationException(
                $"'{entry.Name}' ({entry.Guid}) is not on the Forge, so it cannot be fetched. Install version {entry.ServerVersion} from the author's page."
            );
        }

        var match = (await forge.GetModVersionsAsync(found.Id, cancellationToken)).FirstOrDefault(version => version.Version == wanted);

        if (match is null)
        {
            throw new InvalidOperationException($"The Forge has no version {entry.ServerVersion} of '{entry.Name}'.");
        }

        await installer.InstallAsync(
            new ModInstallRequest
            {
                InstanceId = instanceId,
                DownloadUrl = new Uri(match.DownloadUrl),
                ForgeId = found.Id,
                ExpectedVersion = wanted,
                Pin = entry.Status == ServerSyncStatus.PinnedDiffers,
            },
            cancellationToken
        );
    }

    private async Task<(Instance Instance, string? LastProfileId)> LoadAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instance =
                    await context
                        .Instances.AsNoTracking()
                        .Include(row => row.InstalledMods)
                        .Include(row => row.ServerProfile)
                        .FirstOrDefaultAsync(row => row.Id == instanceId, token)
                    ?? throw new InvalidOperationException("That instance does not exist.");

                return (instance, instance.ServerProfile?.LastProfileId);
            },
            cancellationToken
        );
    }

    private static async Task<IReadOnlyList<SptProfileMod>> ReadClientModsAsync(
        ISptServerClient client,
        string? lastProfileId,
        CancellationToken cancellationToken
    )
    {
        if (lastProfileId is null)
        {
            return [];
        }

        var profile = (await client.GetProfilesAsync(cancellationToken)).FirstOrDefault(row => row.ProfileId == lastProfileId);
        return profile?.ClientMods ?? [];
    }

    private static List<ServerSyncEntry> CompareServerMods(Instance instance, IReadOnlyList<SptServerMod> serverMods)
    {
        var local = instance
            .InstalledMods.Where(mod => !mod.IsAddon && mod.Kind is ModKind.Server or ModKind.Both)
            .ToDictionary(mod => mod.Guid, StringComparer.OrdinalIgnoreCase);

        var entries = new List<ServerSyncEntry>();

        foreach (var mod in serverMods)
        {
            if (!local.Remove(mod.ModGuid, out var installed))
            {
                entries.Add(
                    new ServerSyncEntry
                    {
                        Guid = mod.ModGuid,
                        Name = mod.Name,
                        ServerVersion = mod.Version,
                        Status = ServerSyncStatus.Missing,
                    }
                );
                continue;
            }

            var same = SemanticVersion.TryParse(mod.Version, out var serverVersion) && serverVersion == installed.Version;

            entries.Add(
                new ServerSyncEntry
                {
                    Guid = mod.ModGuid,
                    Name = mod.Name,
                    ServerVersion = mod.Version,
                    LocalVersion = installed.Version.ToString(),
                    Status =
                        same ? ServerSyncStatus.InSync
                        : installed.IsPinned ? ServerSyncStatus.PinnedDiffers
                        : ServerSyncStatus.VersionDiffers,
                    Detail = !same && installed.IsPinned ? "The installed version is pinned." : null,
                }
            );
        }

        entries.AddRange(
            local.Values.Select(installed => new ServerSyncEntry
            {
                Guid = installed.Guid,
                Name = installed.Name,
                LocalVersion = installed.Version.ToString(),
                Status = ServerSyncStatus.LocalOnly,
            })
        );

        return [.. entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static List<ClientModEntry> CompareClientMods(Instance instance, IReadOnlyList<SptProfileMod> recorded)
    {
        var local = instance.InstalledMods.Where(mod => !mod.IsAddon && mod.Kind is ModKind.Client or ModKind.Both).ToList();
        var entries = new List<ClientModEntry>();
        var matched = new HashSet<Guid>();

        foreach (var mod in recorded)
        {
            // The server records the plugin's display name, not its GUID, so the match is by name (or GUID when they agree).
            var installed = local.FirstOrDefault(row =>
                string.Equals(row.Name, mod.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Guid, mod.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (installed is null)
            {
                entries.Add(
                    new ClientModEntry
                    {
                        Name = mod.Name,
                        RecordedVersion = mod.Version,
                        Status = ServerSyncStatus.Missing,
                    }
                );
                continue;
            }

            matched.Add(installed.Id);
            var same =
                mod.Version is null
                || (SemanticVersion.TryParse(mod.Version, out var recordedVersion) && recordedVersion == installed.Version);

            entries.Add(
                new ClientModEntry
                {
                    Name = mod.Name,
                    RecordedVersion = mod.Version,
                    LocalVersion = installed.Version.ToString(),
                    Status = same ? ServerSyncStatus.InSync : ServerSyncStatus.VersionDiffers,
                }
            );
        }

        // Only worth listing when the server recorded something: otherwise every client mod would look "local only" before the first game.
        if (recorded.Count > 0)
        {
            entries.AddRange(
                local
                    .Where(row => !matched.Contains(row.Id))
                    .Select(row => new ClientModEntry
                    {
                        Name = row.Name,
                        LocalVersion = row.Version.ToString(),
                        Status = ServerSyncStatus.LocalOnly,
                    })
            );
        }

        return [.. entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
