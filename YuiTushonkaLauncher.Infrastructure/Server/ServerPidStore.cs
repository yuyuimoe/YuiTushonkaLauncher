using System.Text.Json;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>What the launcher writes about a server it started, so a later launcher can tell a leftover from a stranger.</summary>
public sealed record ServerPidRecord(
    Guid InstanceId,
    int ProcessId,
    DateTimeOffset StartedAt,
    string ExecutablePath,
    int Port,
    int LauncherProcessId
);

/// <summary>One small JSON file per running server under <c>run/</c> in the data folder. Removed on a clean stop.</summary>
public sealed class ServerPidStore(IAppPaths paths, ILogger<ServerPidStore> logger)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string Directory
    {
        get { return Path.Combine(paths.DataRoot, "run"); }
    }

    public void Write(ServerPidRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(record.InstanceId);
        var temp = path + ".tmp";

        File.WriteAllText(temp, JsonSerializer.Serialize(record, Json));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(Guid instanceId)
    {
        try
        {
            File.Delete(PathFor(instanceId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The server record of instance {Instance} could not be removed", instanceId);
        }
    }

    /// <summary>Every record that can be read. A damaged file is skipped, not deleted: it is evidence.</summary>
    public IReadOnlyList<ServerPidRecord> ReadAll()
    {
        var records = new List<ServerPidRecord>();

        if (!System.IO.Directory.Exists(Directory))
        {
            return records;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "server-*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<ServerPidRecord>(File.ReadAllText(file)) is { } record && record.ProcessId > 0)
                {
                    records.Add(record);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "The server record '{File}' is unreadable and was ignored", file);
            }
        }

        return records;
    }

    private string PathFor(Guid instanceId)
    {
        return Path.Combine(Directory, $"server-{instanceId:n}.json");
    }
}
