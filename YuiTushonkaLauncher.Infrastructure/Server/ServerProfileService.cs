using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Profile operations of an instance's running server. The server owns its files, so everything goes through its API.
/// The server checks nothing (it accepts an unknown edition and an empty name, measured), so the checks are here, and a
/// destructive change needs explicit confirmation and leaves a copy of the profile file behind.
/// </summary>
public sealed class ServerProfileService(
    IInstanceService instances,
    ISptServerLocator locator,
    ISptServerClientFactory clients,
    ISptServerSupervisor supervisor,
    IUnitOfWork unitOfWork,
    IAppPaths paths,
    TimeProvider time
) : IServerProfileService
{
    private const int MaxNameLength = 64;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SptProfileSummary>> ListAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default,
        ServerEndpoint? server = null
    )
    {
        var (_, _, client) = await ConnectAsync(instanceId, cancellationToken, server);
        return await client.GetProfilesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SptProfileSummary>> CreateAsync(
        Guid instanceId,
        string username,
        string edition,
        CancellationToken cancellationToken = default,
        ServerEndpoint? server = null
    )
    {
        var name = ValidateName(username);
        var (_, _, client) = await ConnectAsync(instanceId, cancellationToken, server);

        var editions = await client.GetProfileTypesAsync(cancellationToken);

        if (!editions.ContainsKey(edition))
        {
            throw new ProfileOperationException(
                $"'{edition}' is not an edition this server offers. Choose one of: {string.Join(", ", editions.Keys.Order(StringComparer.Ordinal))}."
            );
        }

        var existing = await client.GetProfilesAsync(cancellationToken);

        if (existing.Any(profile => string.Equals(profile.Username, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ProfileOperationException($"A profile named '{name}' already exists on this server.");
        }

        var change = await client.RegisterAsync(name, edition, cancellationToken);

        return change.Accepted
            ? change.Profiles
            : throw new ProfileOperationException($"The server refused to create the profile '{name}'.");
    }

    /// <inheritdoc />
    public async Task<ProfileOperationResult> DeleteAsync(
        Guid instanceId,
        string username,
        bool confirmed,
        CancellationToken cancellationToken = default,
        ServerEndpoint? server = null
    )
    {
        RequireConfirmation(confirmed, "Deleting", username);
        var (instance, location, client) = await ConnectAsync(instanceId, cancellationToken, server);
        var profile = await FindAsync(client, username, cancellationToken);
        var backup = location is { } localFiles ? Backup(instance.Name, localFiles, profile) : null;

        var change = await client.RemoveAsync(profile.Username, cancellationToken);

        if (!change.Accepted)
        {
            throw new ProfileOperationException($"The server refused to delete '{profile.Username}'.{Because(backup)}");
        }

        await ForgetLastProfileAsync(instanceId, profile.ProfileId, cancellationToken);
        return new ProfileOperationResult
        {
            Accepted = true,
            Profiles = change.Profiles,
            BackupPath = backup,
        };
    }

    /// <inheritdoc />
    public async Task<ProfileOperationResult> WipeAsync(
        Guid instanceId,
        string username,
        bool confirmed,
        CancellationToken cancellationToken = default,
        ServerEndpoint? server = null
    )
    {
        RequireConfirmation(confirmed, "Wiping", username);
        var (instance, location, client) = await ConnectAsync(instanceId, cancellationToken, server);
        var profile = await FindAsync(client, username, cancellationToken);
        var backup = location is { } localFiles ? Backup(instance.Name, localFiles, profile) : null;

        var change = await client.WipeAsync(profile.Username, profile.Edition, cancellationToken);

        if (!change.Accepted)
        {
            throw new ProfileOperationException($"The server refused to wipe '{profile.Username}'.{Because(backup)}");
        }

        return new ProfileOperationResult
        {
            Accepted = true,
            Profiles = change.Profiles,
            BackupPath = backup,
        };
    }

    /// <inheritdoc />
    public async Task<SptProfileSummary> SelectAsync(
        Guid instanceId,
        string username,
        CancellationToken cancellationToken = default,
        ServerEndpoint? server = null
    )
    {
        var (_, _, client) = await ConnectAsync(instanceId, cancellationToken, server);
        var profile = await FindAsync(client, username, cancellationToken);

        if (profile.InvalidOrUnloadable)
        {
            throw new ProfileOperationException($"The server could not load the profile '{profile.Username}', so it cannot be selected.");
        }

        if (!await client.LoginAsync(profile.Username, cancellationToken))
        {
            throw new ProfileOperationException($"The server did not accept the profile '{profile.Username}'.");
        }

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.ServerProfiles.FirstOrDefaultAsync(item => item.InstanceId == instanceId, token);

                if (row is not null)
                {
                    row.LastProfileId = profile.ProfileId;
                    row.UpdatedAt = time.GetUtcNow();
                }
            },
            cancellationToken
        );

        return profile;
    }

    /// <inheritdoc />
    public async Task<SptProfileSummary?> GetLastUsedAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var lastId = await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .ServerProfiles.AsNoTracking()
                    .Where(item => item.InstanceId == instanceId)
                    .Select(item => item.LastProfileId)
                    .FirstOrDefaultAsync(token),
            cancellationToken
        );

        if (lastId is null)
        {
            return null;
        }

        var (_, _, client) = await ConnectAsync(instanceId, cancellationToken, server: null);
        return (await client.GetProfilesAsync(cancellationToken)).FirstOrDefault(profile => profile.ProfileId == lastId);
    }

    /// <inheritdoc />
    public async Task<string?> GetRememberedUsernameAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance =
            (await instances.ListAsync(cancellationToken)).FirstOrDefault(row => row.Id == instanceId)
            ?? throw new ProfileOperationException("That instance does not exist.");
        var endpoint = locator.Locate(instance).Endpoint.ToStringKey();

        var remembered = await unitOfWork.ExecuteAsync(
            async (context, token) =>
                await context
                    .ServerProfiles.AsNoTracking()
                    .Where(row => row.InstanceId == instanceId)
                    .Select(row => new { row.RememberedProfileUsername, row.RememberedForEndpoint })
                    .FirstOrDefaultAsync(token),
            cancellationToken
        );

        // A profile belongs to one server: pointing the instance elsewhere drops the preference instead of reusing a name.
        return remembered is not null && string.Equals(remembered.RememberedForEndpoint, endpoint, StringComparison.Ordinal)
            ? remembered.RememberedProfileUsername
            : null;
    }

    /// <inheritdoc />
    public async Task RememberAsync(Guid instanceId, string? username, string? profileId, CancellationToken cancellationToken = default)
    {
        var instance =
            (await instances.ListAsync(cancellationToken)).FirstOrDefault(row => row.Id == instanceId)
            ?? throw new ProfileOperationException("That instance does not exist.");
        var endpoint = locator.Locate(instance).Endpoint.ToStringKey();

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.ServerProfiles.FirstOrDefaultAsync(item => item.InstanceId == instanceId, token);

                if (row is null)
                {
                    return;
                }

                row.RememberedProfileUsername = username;
                row.RememberedProfileId = username is null ? null : profileId;
                row.RememberedForEndpoint = username is null ? null : endpoint;
                row.UpdatedAt = time.GetUtcNow();
            },
            cancellationToken
        );
    }

    private async Task<(Core.Domain.Instance Instance, SptServerLocation? Location, ISptServerClient Client)> ConnectAsync(
        Guid instanceId,
        CancellationToken cancellationToken,
        ServerEndpoint? server
    )
    {
        var instance =
            (await instances.ListAsync(cancellationToken)).FirstOrDefault(row => row.Id == instanceId)
            ?? throw new ProfileOperationException("That instance does not exist.");
        var location = locator.Locate(instance);

        // A server elsewhere is somebody else's process: nothing local to check, and no profile file to back up.
        if (server is { } remote && !IsLocalEndpoint(remote))
        {
            var remoteClient = clients.Create(remote);

            if (!await remoteClient.PingAsync(cancellationToken))
            {
                (remoteClient as IDisposable)?.Dispose();
                throw new ServerNotRunningException(
                    $"The server at {remote.BaseUri} is not answering. Check the address and that it is running."
                );
            }

            return (instance, null, remoteClient);
        }

        if (supervisor.GetStatus(instanceId).State is Core.Server.ServerState.Stopped or Core.Server.ServerState.Failed)
        {
            // A server started by hand still counts, so the probe below decides; this only refuses the wrong server.
            foreach (var other in (await instances.ListAsync(cancellationToken)).Where(row => row.Id != instanceId))
            {
                if (
                    supervisor.GetStatus(other.Id) is { State: Core.Server.ServerState.Running, Endpoint: { } running }
                    && running.Port == location.Endpoint.Port
                )
                {
                    throw new ServerNotRunningException(
                        $"The server on port {running.Port} belongs to instance '{other.Name}', not '{instance.Name}'. Start the server of '{instance.Name}' on its own port."
                    );
                }
            }
        }

        var client = clients.Create(location.Endpoint);

        if (!await client.PingAsync(cancellationToken))
        {
            (client as IDisposable)?.Dispose();
            throw new ServerNotRunningException(
                $"The server of '{instance.Name}' is not answering at {location.Endpoint.BaseUri}. Start it first."
            );
        }

        return (instance, location, client);
    }

    private static bool IsLocalEndpoint(ServerEndpoint endpoint)
    {
        return endpoint.ConnectHost.Trim('[', ']').ToLowerInvariant() switch
        {
            "127.0.0.1" or "localhost" or "::1" => true,
            _ => false,
        };
    }

    private static async Task<SptProfileSummary> FindAsync(ISptServerClient client, string username, CancellationToken cancellationToken)
    {
        var profiles = await client.GetProfilesAsync(cancellationToken);

        return profiles.FirstOrDefault(profile => string.Equals(profile.Username, username, StringComparison.Ordinal))
            ?? throw new ProfileOperationException(
                $"The server has no profile named '{username}'. It has: {(profiles.Count == 0 ? "none" : string.Join(", ", profiles.Select(profile => profile.Username)))}."
            );
    }

    private static void RequireConfirmation(bool confirmed, string verb, string username)
    {
        if (!confirmed)
        {
            throw new ProfileOperationException(
                $"{verb} the profile '{username}' destroys its progress and was not confirmed. A copy of the file is kept, but confirm explicitly."
            );
        }
    }

    private static string ValidateName(string username)
    {
        var name = username?.Trim() ?? string.Empty;

        if (name.Length == 0 || name.Length > MaxNameLength || name.Any(char.IsControl))
        {
            throw new ProfileOperationException($"A profile name must be 1 to {MaxNameLength} characters, with no control characters.");
        }

        return name;
    }

    /// <summary>Copies the profile file next to the launcher's data before anything destructive happens, and proves the copy.</summary>
    /// <summary>What a refusal can promise about a copy: only a local server leaves a file to copy.</summary>
    private static string Because(string? backup)
    {
        return backup is null ? " Nothing was copied: the profile lives on another machine." : $" A backup is at '{backup}'.";
    }

    private string? Backup(string instanceName, SptServerLocation location, SptProfileSummary profile)
    {
        var source = Path.Combine(location.ProfilesDirectory, profile.ProfileId + ".json");

        if (!File.Exists(source))
        {
            // A profile that was never saved has no file, so there is nothing to lose.
            return null;
        }

        var directory = Path.Combine(paths.DataRoot, "backups", "profiles", instanceName);
        Directory.CreateDirectory(directory);

        var stamp = time.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(directory, $"{profile.ProfileId}-{stamp}.json");

        File.Copy(source, target, overwrite: false);

        if (!HashOf(source).SequenceEqual(HashOf(target)))
        {
            File.Delete(target);
            throw new ProfileOperationException($"The backup of '{source}' did not match the original, so nothing was changed.");
        }

        return target;
    }

    private static byte[] HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private async Task ForgetLastProfileAsync(Guid instanceId, string profileId, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.ServerProfiles.FirstOrDefaultAsync(
                    item => item.InstanceId == instanceId && item.LastProfileId == profileId,
                    token
                );

                if (row is not null)
                {
                    row.LastProfileId = null;
                }
            },
            cancellationToken
        );
    }
}
