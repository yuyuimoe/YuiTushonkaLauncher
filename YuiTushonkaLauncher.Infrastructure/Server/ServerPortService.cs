using Microsoft.EntityFrameworkCore;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Chooses and changes the port of an instance's server. <c>http.json</c> is the source of truth (it is what the
/// server reads); the database row only mirrors it. The write goes through the copy-on-write writer.
/// </summary>
public sealed class ServerPortService(
    IInstanceService instances,
    ISptServerLocator locator,
    IPortProbe ports,
    ISptServerSupervisor supervisor,
    ILinkAwareFileWriter writer,
    IUnitOfWork unitOfWork,
    TimeProvider time
) : IServerPortService
{
    private const int FirstUnprivilegedPort = 1024;

    private const int SearchWidth = 500;

    /// <summary>The first port at or after <paramref name="start" /> that is free and not in <paramref name="reserved" />.</summary>
    public static int FindFree(int start, IPortProbe ports, ISet<int> reserved)
    {
        var last = Math.Min(65535, start + SearchWidth);

        for (var candidate = Math.Max(start, FirstUnprivilegedPort); candidate <= last; candidate++)
        {
            if (!reserved.Contains(candidate) && ports.IsFree(candidate))
            {
                return candidate;
            }
        }

        throw new ServerPortException($"No free port was found between {start} and {last}.");
    }

    /// <inheritdoc />
    public async Task<int> SuggestFreePortAsync(int start = ServerEndpoint.DefaultPort, CancellationToken cancellationToken = default)
    {
        var configured = new HashSet<int>();

        foreach (var instance in await instances.ListAsync(cancellationToken))
        {
            configured.Add(locator.Locate(instance).Endpoint.Port);
        }

        return FindFree(start, ports, configured);
    }

    /// <inheritdoc />
    public async Task SetPortAsync(Guid instanceId, int port, CancellationToken cancellationToken = default)
    {
        if (port is < FirstUnprivilegedPort or > 65535)
        {
            throw new ServerPortException($"Port {port} is not usable: pick one from {FirstUnprivilegedPort} to 65535.");
        }

        var all = await instances.ListAsync(cancellationToken);
        var instance = all.FirstOrDefault(row => row.Id == instanceId) ?? throw new ServerPortException("That instance does not exist.");

        if (
            supervisor.GetStatus(instanceId).State
            is Core.Server.ServerState.Starting
                or Core.Server.ServerState.Running
                or Core.Server.ServerState.Stopping
        )
        {
            throw new ServerPortException($"The server of '{instance.Name}' is running; stop it before changing its port.");
        }

        var location = locator.Locate(instance);

        if (!File.Exists(location.ConfigPath))
        {
            throw new ServerPortException($"The server configuration '{location.ConfigPath}' does not exist.");
        }

        var owner = all.FirstOrDefault(other => other.Id != instanceId && locator.Locate(other).Endpoint.Port == port);

        if (owner is not null)
        {
            throw new ServerPortException($"Port {port} is configured for instance '{owner.Name}'.");
        }

        if (port != location.Endpoint.Port && !ports.IsFree(port))
        {
            throw new ServerPortException($"Port {port} is in use by another program right now.");
        }

        await writer.WriteAllTextAsync(location.ConfigPath, ServerHttpConfig.WithPort(location.ConfigPath, port), cancellationToken);

        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.Instances.Include(item => item.ServerProfile).FirstAsync(item => item.Id == instanceId, token);
                row.ServerPort = port;

                if (row.ServerProfile is { } profile)
                {
                    profile.Port = port;
                    profile.BackendUrl = $"https://127.0.0.1:{port}";
                    profile.UpdatedAt = time.GetUtcNow();
                }
            },
            cancellationToken
        );
    }
}
