using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>
/// The forge's dependency answers, turned around. <c>/mods/dependencies</c> answers for packages you name; it has no
/// mirror that answers "who needs this mod". So the whole catalogue is asked, once, and the answers are stored: the
/// reverse direction is then a lookup. The walk is the expensive part (about forty pages of mods plus as many batched
/// dependency questions), which is why it is cached for a week and can be watched through
/// <see cref="DependencyGraphProgress" />.
/// </summary>
public sealed class DependencyGraph : IDependencyGraph
{
    /// <summary>How long a built graph is trusted before the catalogue is walked again.</summary>
    public const int FreshDays = 7;

    private const int PageSize = 50;
    private const int ModsPerQuestion = 40;
    private const int MaxPages = 200;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IForgeClient _forge;
    private readonly ILogger<DependencyGraph> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private volatile IReadOnlyList<ForgeDependencyEdge>? _edges;

    /// <summary>A graph cached under the launcher's data directory.</summary>
    public DependencyGraph(IForgeClient forge, IAppPaths paths, ILogger<DependencyGraph> logger)
    {
        _forge = forge;
        _logger = logger;
        _path = Path.Combine(paths.CacheDirectory, "dependency-graph.json");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeDependencyEdge>> DependentsOfAsync(
        int modId,
        IProgress<DependencyGraphProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var edges = await LoadAsync(progress, cancellationToken);
        return [.. edges.Where(edge => edge.ToId == modId)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeDependencyEdge>> DependenciesOfAsync(
        int modId,
        IProgress<DependencyGraphProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var edges = await LoadAsync(progress, cancellationToken);
        return [.. edges.Where(edge => edge.FromId == modId)];
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        _edges = null;
        try
        {
            File.Delete(_path);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not delete the dependency graph at {Path}.", _path);
        }
    }

    private async Task<IReadOnlyList<ForgeDependencyEdge>> LoadAsync(
        IProgress<DependencyGraphProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        if (_edges is { } loaded)
        {
            return loaded;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_edges is { } raced)
            {
                return raced;
            }

            if (await ReadCacheAsync(cancellationToken) is { } cached)
            {
                _edges = cached;
                return cached;
            }

            var built = await BuildAsync(progress, cancellationToken);
            _edges = built;
            return built;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ForgeDependencyEdge>?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var cache = await JsonSerializer.DeserializeAsync<CacheFile>(stream, Options, cancellationToken);
            if (cache is null || cache.Edges.Count == 0)
            {
                return null;
            }

            if (DateTimeOffset.UtcNow - cache.BuiltAt > TimeSpan.FromDays(FreshDays))
            {
                // Stale answers are still answers: they are used, and the next build replaces them.
                _logger.LogInformation("The dependency graph is older than {Days} days; rebuilding on next use.", FreshDays);
                return null;
            }

            return [.. cache.Edges.Select(edge => edge.ToModel())];
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            _logger.LogWarning(exception, "The dependency graph at {Path} could not be read; it will be rebuilt.", _path);
            return null;
        }
    }

    private async Task<IReadOnlyList<ForgeDependencyEdge>> BuildAsync(
        IProgress<DependencyGraphProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var sptVersion = await SptVersionAsync(cancellationToken);
        var edges = new List<ForgeDependencyEdge>();
        var page = 1;
        var pages = 1;
        while (page <= pages && page <= MaxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageOfMods = await _forge.GetModsAsync(
                new ForgeModQuery
                {
                    Page = page,
                    PerPage = PageSize,
                    IncludeVersions = true,
                },
                cancellationToken
            );

            pages = Math.Max(1, pageOfMods.LastPage);
            var packages = pageOfMods
                .Items.Select(mod => (Mod: mod, Version: Newest(mod)))
                .Where(pair => pair.Version is not null && !string.IsNullOrWhiteSpace(pair.Mod.Guid))
                .Select(pair => new InstalledPackage { Guid = pair.Mod.Guid!, Version = pair.Version!.Value })
                .ToList();

            foreach (var chunk in packages.Chunk(ModsPerQuestion))
            {
                var answers = await _forge.GetDependenciesAsync(chunk, sptVersion, cancellationToken);
                foreach (var package in chunk)
                {
                    // The API keys its answer by what was asked, "<guid>:<version>"; a bare GUID is the fallback.
                    if (
                        !answers.TryGetValue($"{package.Guid}:{package.Version}", out var dependencies)
                        && !answers.TryGetValue(package.Guid, out dependencies)
                    )
                    {
                        continue;
                    }

                    var owner = pageOfMods.Items.FirstOrDefault(mod =>
                        string.Equals(mod.Guid, package.Guid, StringComparison.OrdinalIgnoreCase)
                    );
                    if (owner is null)
                    {
                        continue;
                    }

                    foreach (var dependency in dependencies.Where(dependency => dependency.ModId is > 0))
                    {
                        edges.Add(
                            new ForgeDependencyEdge
                            {
                                FromId = owner.Id,
                                FromGuid = owner.Guid,
                                FromName = owner.Name,
                                ToId = dependency.ModId!.Value,
                                ToGuid = dependency.Guid,
                                ToName = dependency.Name ?? dependency.Guid ?? "?",
                            }
                        );
                    }
                }
            }

            progress?.Report(new DependencyGraphProgress(page, pages, edges.Count));
            page++;
        }

        _logger.LogInformation("Dependency graph built: {Edges} relations over {Pages} pages.", edges.Count, pages);
        await WriteCacheAsync(edges, cancellationToken);
        return edges;
    }

    private async Task<SemanticVersion> SptVersionAsync(CancellationToken cancellationToken)
    {
        // The endpoint insists on an SPT version even though its answer barely depends on it; the newest release the
        // catalogue lists is the safest thing to name when no instance is open.
        var versions = await _forge.GetSptVersionsAsync(1, cancellationToken);
        return versions.Count > 0 ? versions[0].Version : new SemanticVersion(4, 0, 0);
    }

    private static SemanticVersion? Newest(ForgeMod mod)
    {
        SemanticVersion? newest = null;
        foreach (var version in mod.Versions)
        {
            if (newest is null || version.Version > newest)
            {
                newest = version.Version;
            }
        }

        return newest;
    }

    private async Task WriteCacheAsync(IReadOnlyList<ForgeDependencyEdge> edges, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new CacheFile { BuiltAt = DateTimeOffset.UtcNow, Edges = [.. edges.Select(edge => CacheEdge.From(edge))] },
                    Options,
                    cancellationToken
                );
            }

            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The dependency graph could not be written to {Path}.", _path);
        }
    }

    private sealed class CacheFile
    {
        public DateTimeOffset BuiltAt { get; set; }

        public List<CacheEdge> Edges { get; set; } = [];
    }

    private sealed class CacheEdge
    {
        public int FromId { get; set; }

        public string? FromGuid { get; set; }

        public string FromName { get; set; } = string.Empty;

        public int ToId { get; set; }

        public string? ToGuid { get; set; }

        public string ToName { get; set; } = string.Empty;

        public static CacheEdge From(ForgeDependencyEdge edge)
        {
            return new CacheEdge
            {
                FromId = edge.FromId,
                FromGuid = edge.FromGuid,
                FromName = edge.FromName,
                ToId = edge.ToId,
                ToGuid = edge.ToGuid,
                ToName = edge.ToName,
            };
        }

        public ForgeDependencyEdge ToModel()
        {
            return new ForgeDependencyEdge
            {
                FromId = FromId,
                FromGuid = FromGuid,
                FromName = FromName,
                ToId = ToId,
                ToGuid = ToGuid,
                ToName = ToName,
            };
        }
    }
}
