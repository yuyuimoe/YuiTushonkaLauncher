using System.Text.Json;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>One small JSON file per instance under <c>run/</c>: the server and the game it belongs with.</summary>
public sealed class InstanceRunStore(IAppPaths paths, ILogger<InstanceRunStore> logger) : IInstanceRunStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string Directory
    {
        get { return Path.Combine(paths.DataRoot, "run"); }
    }

    /// <inheritdoc />
    public void Save(InstanceRun run)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(run.InstanceId);
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(
            run with
            {
                UpdatedAt = run.UpdatedAt == default ? DateTimeOffset.UtcNow : run.UpdatedAt,
            },
            Json
        );

        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    /// <inheritdoc />
    public void Forget(Guid instanceId)
    {
        try
        {
            File.Delete(PathFor(instanceId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The run record of instance {Instance} could not be removed", instanceId);
        }
    }

    /// <inheritdoc />
    public InstanceRun? Find(Guid instanceId)
    {
        var path = PathFor(instanceId);

        return File.Exists(path) ? Read(path) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<InstanceRun> ReadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var runs = new List<InstanceRun>();
        var seen = new HashSet<Guid>();

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "run-*.json"))
        {
            if (Read(file) is { } run && seen.Add(run.InstanceId))
            {
                runs.Add(run);
            }
        }

        return runs;
    }

    private InstanceRun? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<InstanceRun>(File.ReadAllText(path)) is { InstanceId: var id } run && id != Guid.Empty
                ? run
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged record is evidence of a crash, not something to delete silently.
            logger.LogWarning(exception, "The run record '{Path}' is unreadable and was ignored", path);
            return null;
        }
    }

    private string PathFor(Guid instanceId)
    {
        return Path.Combine(Directory, $"run-{instanceId:n}.json");
    }
}
